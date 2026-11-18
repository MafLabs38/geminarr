import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';
import vm from 'node:vm';

const ORIGIN = 'https://assistant.example';
const SOURCE = readFileSync(new URL('../../../extension/background.js', import.meta.url), 'utf8').replaceAll(
  '__ASSISTANT_ORIGIN__',
  ORIGIN,
);
// What the worker built lives in another realm: a plain copy is what is compared.
const plain = (value) => JSON.parse(JSON.stringify(value));
const flush = async () => {
  for (let i = 0; i < 5; i++) await new Promise((resolve) => setImmediate(resolve));
};

/** The worker, loaded with a stand-in for the browser: its storage, its tabs, its alarms, and the address that Geminarr's calls go to. */
async function load({
  managed = { geminarrUrl: 'https://geminarr.example/', token: 'the-token' },
  answer = () => ({ ok: true, json: async () => ({ open: [] }) }),
  alarms = {},
} = {}) {
  const session = new Map();
  const calls = { fetch: [], created: [], removed: [], alarmsCreated: [], updateChecks: 0 };
  const listeners = {};
  let nextTab = 100;
  const chrome = {
    storage: {
      managed: { get: async () => managed },
      session: {
        get: async (keys) =>
          keys === null
            ? Object.fromEntries(session)
            : Object.fromEntries(
                [keys]
                  .flat()
                  .filter((key) => session.has(key))
                  .map((key) => [key, session.get(key)]),
              ),
        set: async (values) => Object.entries(values).forEach(([key, value]) => session.set(key, value)),
        remove: async (key) => session.delete(key),
      },
    },
    tabs: {
      create: async (options) => {
        calls.created.push(options);
        return { id: nextTab++ };
      },
      remove: async (id) => calls.removed.push(id),
    },
    alarms: {
      get: async (name) => alarms[name],
      create: (name, info) => calls.alarmsCreated.push([name, info.periodInMinutes]),
      onAlarm: { addListener: (fn) => (listeners.alarm = fn) },
    },
    runtime: {
      getManifest: () => ({ version: '1.2.3' }),
      requestUpdateCheck: () => {
        calls.updateChecks++;
        return Promise.reject(new Error('throttled'));
      },
      onMessage: { addListener: (fn) => (listeners.message = fn) },
      onInstalled: { addListener: (fn) => (listeners.installed = fn) },
      onStartup: { addListener: (fn) => (listeners.startup = fn) },
    },
  };
  const context = vm.createContext({
    chrome,
    fetch: async (url, init) => {
      calls.fetch.push({ url, init });
      return answer(url, init);
    },
    JSON,
    Promise,
    Date,
    Number,
    Object,
    String,
  });
  // S1523 (code-eval), by design: this runs the extension's own file, read from this repository, in a context that holds nothing but stand-ins.
  // eslint-disable-next-line sonarjs/code-eval -- the extension's own source, in a vm context with stand-ins
  vm.runInContext(SOURCE, context); // NOSONAR
  await flush();
  return { calls, listeners, session };
}

describe('what the extension sends', () => {
  it('sends to the address of its policy only, with the token, the header of a write and its version', async () => {
    const { calls, listeners } = await load();

    listeners.message({ kind: 'state', signedIn: false }, {});
    await flush();

    assert.equal(calls.fetch.length, 1);
    assert.equal(calls.fetch[0].url, 'https://geminarr.example/api/v1/extension/push');
    assert.equal(calls.fetch[0].init.headers.authorization, 'Bearer the-token');
    assert.equal(calls.fetch[0].init.headers['x-geminarr-request'], '1');
    assert.deepEqual(JSON.parse(calls.fetch[0].init.body), { kind: 'state', signedIn: false, version: '1.2.3' });
  });

  it('sends nothing, and opens nothing, without an address and a token', async () => {
    const { calls, listeners } = await load({ managed: { geminarrUrl: 'https://geminarr.example' } });

    listeners.message({ kind: 'state', signedIn: false }, {});
    listeners.startup();
    await flush();

    assert.deepEqual([calls.fetch.length, calls.created.length], [0, 0]);
  });

  it('is not troubled by a Geminarr that refuses or does not answer', async () => {
    for (const answer of [() => ({ ok: false }), () => Promise.reject(new Error('down'))]) {
      const { calls, listeners } = await load({ answer });

      listeners.message({ kind: 'list', items: [] }, {});
      await flush();

      assert.equal(calls.created.length, 0);
    }
  });
});

describe('what the extension opens and closes', () => {
  it('opens a page in the background to read the list when a round starts, and remembers that the page is its own', async () => {
    const { calls, listeners, session } = await load();

    listeners.alarm({ name: 'round' });
    await flush();

    assert.deepEqual(plain(calls.created), [{ url: `${ORIGIN}/app`, active: false }]);
    assert.deepEqual([...session.keys()], ['workTab:100']);
  });

  it('opens the conversations that Geminarr asks for, and only ids that are ids, and closes the page of the list', async () => {
    const { calls, listeners, session } = await load({
      answer: () => ({
        ok: true,
        json: async () => ({ open: ['abc', 'has space', '../x', 'a'.repeat(65), 'def_-1'] }),
      }),
    });
    listeners.alarm({ name: 'round' });
    await flush();
    calls.created.length = 0;

    listeners.message({ kind: 'list', items: [] }, { tab: { id: 100 } });
    await flush();

    assert.deepEqual(
      calls.created.map((tab) => tab.url),
      [`${ORIGIN}/app/abc`, `${ORIGIN}/app/def_-1`],
    );
    assert.deepEqual(calls.removed, [100]);
    assert.ok(!session.has('workTab:100'));
  });

  it('opens at most ten conversations for one answer, whatever the answer says', async () => {
    const many = Array.from({ length: 30 }, (_, i) => `c${i}`);
    const { calls, listeners } = await load({ answer: () => ({ ok: true, json: async () => ({ open: many }) }) });
    listeners.alarm({ name: 'round' });
    await flush();
    calls.created.length = 0;

    listeners.message({ kind: 'list', items: [] }, { tab: { id: 100 } });
    await flush();

    assert.equal(calls.created.length, 10);
  });

  it('opens nothing, and still closes its page, when the answer asks for nothing', async () => {
    const { calls, listeners } = await load({ answer: () => ({ ok: true, json: async () => ({}) }) });
    listeners.alarm({ name: 'round' });
    await flush();
    calls.created.length = 0;

    listeners.message({ kind: 'list', items: [] }, { tab: { id: 100 } });
    await flush();

    assert.deepEqual([calls.created.length, calls.removed], [0, [100]]);
  });

  it('does not start a round while the last one is still reading', async () => {
    const { calls, listeners } = await load();

    listeners.alarm({ name: 'round' });
    await flush();
    listeners.alarm({ name: 'round' });
    listeners.startup();
    await flush();

    assert.equal(calls.created.length, 1);
  });

  it('closes a page that it opened once it has said what it had to say, whatever it said', async () => {
    for (const message of [
      { kind: 'conversation', id: 'abc', turns: [] },
      { kind: 'state', signedIn: false },
      { kind: 'broken', step: 'page' },
    ]) {
      const { calls, listeners } = await load();
      listeners.alarm({ name: 'round' });
      await flush();

      listeners.message(message, { tab: { id: 100 } });
      await flush();

      assert.deepEqual(calls.removed, [100], message.kind);
    }
  });

  it('leaves alone the page that a person opened', async () => {
    const { calls, listeners } = await load();

    listeners.message({ kind: 'list', items: [] }, { tab: { id: 7 } });
    await flush();

    assert.equal(calls.fetch.length, 1, 'what it read is still handed over');
    assert.deepEqual(calls.removed, []);
  });

  it('closes a page that has not answered in three minutes, and keeps the others', async () => {
    const { calls, listeners, session } = await load();
    session.set('workTab:7', Date.now() - 4 * 60 * 1000);
    session.set('workTab:8', Date.now() - 1000);
    session.set('somethingElse', 0);

    listeners.alarm({ name: 'sweep' });
    await flush();

    assert.deepEqual(calls.removed, [7]);
    assert.deepEqual([...session.keys()].sort(), ['somethingElse', 'workTab:8']);
  });

  it('asks the browser for a new version of itself, and is not troubled by an answer that says no', async () => {
    const { calls, listeners } = await load();

    listeners.alarm({ name: 'update' });
    await flush();

    assert.equal(calls.updateChecks, 1);
  });
});

describe('which pages are its own', () => {
  const ask = async (listeners, tab) => {
    let answer;
    const kept = listeners.message({ kind: 'mine?' }, { tab }, (value) => (answer = value));
    await flush();
    return [kept, answer];
  };

  it('says yes to a page that it opened, and no to any other, and keeps the channel open to answer', async () => {
    const { listeners } = await load();
    listeners.alarm({ name: 'round' });
    await flush();

    assert.deepEqual(await ask(listeners, { id: 100 }), [true, true]);
    assert.deepEqual(await ask(listeners, { id: 7 }), [true, false]);
    assert.deepEqual(await ask(listeners, undefined), [true, false]);
  });
});

describe('the round interval', () => {
  const periodOf = (calls) => calls.alarmsCreated.find(([name]) => name === 'round')?.[1];

  it('is fifteen minutes unless the policy says another whole number of minutes in the allowed range', async () => {
    for (const [wanted, expected] of [
      [undefined, 15],
      [0, 15],
      [1441, 15],
      [2.5, 15],
      ['5', 15],
      [1, 1],
      [5, 5],
      [1440, 1440],
    ]) {
      const { calls } = await load({
        managed: { geminarrUrl: 'https://geminarr.example', token: 't', roundMinutes: wanted },
      });

      assert.equal(periodOf(calls), expected, `roundMinutes ${wanted}`);
    }
  });

  it('does not start an alarm again when it already runs with the period that is wanted', async () => {
    const { calls } = await load({
      alarms: { round: { periodInMinutes: 15 }, sweep: { periodInMinutes: 1 }, update: { periodInMinutes: 60 } },
    });

    assert.deepEqual(calls.alarmsCreated, []);
  });
});
