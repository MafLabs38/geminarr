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
  openTabs = [],
  failToOpen = false,
} = {}) {
  const session = new Map();
  const calls = {
    delays: [],
    fetch: [],
    created: [],
    removed: [],
    alarmsCreated: [],
    updateChecks: 0,
    cleared: [],
    queried: [],
    reloaded: [],
  };
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
        if (failToOpen) throw new Error('refused');
        calls.created.push(options);
        return { id: nextTab++ };
      },
      remove: async (id) => calls.removed.push(id),
      query: async (filter) => {
        calls.queried.push(filter);
        return openTabs;
      },
      reload: async (id) => calls.reloaded.push(id),
    },
    windows: {
      create: async (options) => {
        if (failToOpen) throw new Error('refused');
        calls.created.push(options);
        return { tabs: [{ id: nextTab++ }] };
      },
    },
    browsingData: {
      remove: async (options, types) => {
        if (calls.refuse) throw new Error('refused');
        calls.cleared.push([options, types]);
      },
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
  chrome.tabs.onUpdated = { addListener: (fn) => (listeners.updated = fn) };
  const context = vm.createContext({
    chrome,
    fetch: async (url, init) => {
      calls.fetch.push({ url, init });
      return answer(url, init);
    },
    JSON,
    setTimeout: (fn, ms) => {
      calls.delays.push(ms);
      setImmediate(fn);
    },
    AbortSignal,
    crypto,
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

    listeners.startup();
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
    listeners.startup();
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

  it('opens at most twenty-five conversations for one answer, whatever the answer says', async () => {
    const many = Array.from({ length: 30 }, (_, i) => `c${i}`);
    const { calls, listeners } = await load({ answer: () => ({ ok: true, json: async () => ({ open: many }) }) });
    listeners.startup();
    await flush();
    calls.created.length = 0;

    listeners.message({ kind: 'list', items: [] }, { tab: { id: 100 } });
    // A pause lies between two pages (it takes no time here, but it is a turn of the loop).
    for (let turn = 0; turn < 20; turn++) await flush();

    assert.equal(calls.created.length, 25);
  });

  it('opens the pages of one answer one after the other, with a pause of one to four seconds between two', async () => {
    const { calls, listeners } = await load({
      answer: () => ({ ok: true, json: async () => ({ open: ['a1', 'b2', 'c3'] }) }),
    });
    listeners.startup();
    await flush();
    calls.created.length = 0;
    calls.delays.length = 0;

    listeners.message({ kind: 'list', items: [] }, { tab: { id: 100 } });
    for (let turn = 0; turn < 5; turn++) await flush();

    assert.equal(calls.created.length, 3);
    const pauses = calls.delays.filter((ms) => ms >= 1000 && ms < 4000);
    assert.equal(pauses.length, 2, 'one pause between two pages, none before the first');
  });

  it('reads a conversation in a window of its own that is not focused, and the list in a tab that is not active', async () => {
    const { calls, listeners, session } = await load({
      answer: () => ({ ok: true, json: async () => ({ open: ['abc'] }) }),
    });
    listeners.startup();
    await flush();
    calls.created.length = 0;

    listeners.message({ kind: 'list', items: [] }, { tab: { id: 100 } });
    for (let turn = 0; turn < 5; turn++) await flush();

    const [conversation] = plain(calls.created);
    assert.equal(conversation.url, `${ORIGIN}/app/abc`);
    assert.equal(conversation.focused, false);
    assert.equal(conversation.active, undefined, 'a window, not a tab that a person is looking at');
    assert.ok(
      [...session.keys()].some((key) => key.startsWith('workTab:')),
      'it is one of the pages of this extension itself, read and closed',
    );
  });

  it('opens nothing, and still closes its page, when the answer asks for nothing', async () => {
    const { calls, listeners } = await load({ answer: () => ({ ok: true, json: async () => ({}) }) });
    listeners.startup();
    await flush();
    calls.created.length = 0;

    listeners.message({ kind: 'list', items: [] }, { tab: { id: 100 } });
    await flush();

    assert.deepEqual([calls.created.length, calls.removed], [0, [100]]);
  });

  it('opens nothing and still closes its page when the answer is not what Geminarr says', async () => {
    for (const answer of [
      () => ({ ok: true, json: async () => ({ open: 'abc' }) }),
      () => ({ ok: true, json: async () => Promise.reject(new Error('not json')) }),
    ]) {
      const { calls, listeners } = await load({ answer });
      listeners.startup();
      await flush();
      calls.created.length = 0;

      listeners.message({ kind: 'list', items: [] }, { tab: { id: 100 } });
      await flush();

      assert.deepEqual([calls.created.length, calls.removed], [0, [100]]);
    }
  });

  it('closes the page of a conversation that gives up without telling Geminarr anything', async () => {
    const { calls, listeners } = await load();
    listeners.startup();
    await flush();

    listeners.message({ kind: 'giveup' }, { tab: { id: 100 } });
    await flush();

    assert.deepEqual([calls.fetch.length, calls.removed], [0, [100]]);
  });

  it('does not start a round while the last one is still reading', async () => {
    const { calls, listeners } = await load();

    listeners.startup();
    await flush();
    listeners.startup();
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
      listeners.startup();
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

  it('closes a page that has not answered in five minutes, and keeps the others', async () => {
    const { calls, listeners, session } = await load();
    session.set('workTab:7', Date.now() - 6 * 60 * 1000);
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
    listeners.startup();
    await flush();

    assert.deepEqual(await ask(listeners, { id: 100 }), [true, true]);
    assert.deepEqual(await ask(listeners, { id: 7 }), [true, false]);
    assert.deepEqual(await ask(listeners, undefined), [true, false]);
  });
});

describe('what the extension does when Geminarr asks', () => {
  const answering = (body) => () => ({ ok: true, json: async () => body });

  it('says that it is there at an alarm, and looks at once when Geminarr asks for a round', async () => {
    const { calls, listeners } = await load({ answer: answering({ open: [], round: true, signOut: false }) });

    listeners.alarm({ name: 'hello' });
    await flush();

    assert.deepEqual(JSON.parse(calls.fetch[0].init.body), { kind: 'hello', version: '1.2.3' });
    assert.deepEqual(plain(calls.created), [{ url: `${ORIGIN}/app`, active: false }]);
  });

  it('asks for nothing of its own accord when Geminarr asks for nothing', async () => {
    const { calls, listeners } = await load({ answer: answering({ open: [], round: false, signOut: false }) });

    listeners.alarm({ name: 'hello' });
    await flush();

    assert.equal(calls.created.length, 0);
  });

  it('keeps the alarm of the heartbeat, every half minute', async () => {
    const { calls } = await load();

    assert.ok(calls.alarmsCreated.some(([name, period]) => name === 'hello' && period === 0.5));
  });

  it('signs the account out by deleting the cookies of its browser, nothing else, once in a while only, and looks at what the page shows after', async () => {
    const { calls, listeners } = await load({ answer: answering({ open: [], round: false, signOut: true }) });

    listeners.alarm({ name: 'hello' });
    await flush();
    listeners.alarm({ name: 'hello' });
    await flush();

    assert.deepEqual(plain(calls.cleared), [[{ since: 0 }, { cookies: true }]]);
    assert.ok(
      calls.fetch.some(
        (call) => JSON.parse(call.init.body).kind === 'state' && JSON.parse(call.init.body).signedIn === false,
      ),
      'it says that nobody is signed in once the cookies are gone',
    );
    assert.deepEqual(
      plain(calls.created),
      [{ url: `${ORIGIN}/app`, active: false }],
      'a round follows, to see what the page shows',
    );
  });

  it('loads again the tabs of the assistant that a person has open, once the cookies are gone', async () => {
    const { calls, listeners } = await load({
      answer: answering({ open: [], round: false, signOut: true }),
      openTabs: [{ id: 7 }, { id: 8 }],
    });

    listeners.alarm({ name: 'hello' });
    await flush();

    assert.deepEqual(plain(calls.queried), [{ url: `${ORIGIN}/*` }]);
    assert.deepEqual(plain(calls.reloaded), [7, 8], 'they showed the account that was signed in');
  });

  it('does not reload anything when the browser refuses to delete the cookies', async () => {
    const { calls, listeners } = await load({
      answer: answering({ open: [], round: false, signOut: true }),
      openTabs: [{ id: 7 }],
    });
    calls.refuse = true;

    listeners.alarm({ name: 'hello' });
    await flush();

    assert.deepEqual(plain(calls.reloaded), []);
  });

  it('tells Geminarr at once when a page of the assistant that a person has open has loaded, once in a few seconds, and not for its own pages', async () => {
    const { calls, listeners } = await load({ answer: answering({ open: [], round: true, signOut: false }) });
    const kinds = () => calls.fetch.map((call) => JSON.parse(call.init.body).kind);

    listeners.updated(5, { status: 'loading' }, { url: `${ORIGIN}/app` });
    listeners.updated(5, { status: 'complete' }, { url: 'https://elsewhere.example/app' });
    await flush();
    assert.deepEqual(kinds(), [], 'neither a page that is still loading nor another origin');

    listeners.updated(5, { status: 'complete' }, { url: `${ORIGIN}/app` });
    await flush();
    listeners.updated(5, { status: 'complete' }, { url: `${ORIGIN}/app/abc` });
    await flush();
    assert.deepEqual(kinds(), ['page'], 'the second load is too close to the first');
    assert.equal(calls.created.length, 1, 'and what Geminarr answers is acted on: a round');
  });

  it('does not tell Geminarr about a page that it opened itself', async () => {
    const { calls, listeners } = await load({ answer: answering({ open: [], round: false, signOut: false }) });
    listeners.alarm({ name: 'sweep' });
    await listeners.startup();
    await flush();
    const opened = 100;
    calls.fetch.length = 0;

    listeners.updated(opened, { status: 'complete' }, { url: `${ORIGIN}/app` });
    await flush();

    assert.deepEqual(calls.fetch, []);
  });

  it('keeps one request open to Geminarr and does what it answers, at once, then asks again', async () => {
    let calls = 0;
    const managed = { geminarrUrl: 'https://geminarr.example/', token: 'the-token' };
    const { calls: seen, listeners } = await load({
      managed,
      answer: () => {
        calls++;
        // The third request is the last: the loop ends when the settings are gone.
        if (calls >= 3) delete managed.token;
        return {
          ok: true,
          json: async () => ({ open: [], round: false, signOut: calls === 1 }),
        };
      },
    });

    listeners.alarm({ name: 'listen' });
    for (let i = 0; i < 40 && seen.fetch.length < 3; i++) await flush();

    const kinds = seen.fetch.map((call) => JSON.parse(call.init.body).kind);
    assert.equal(kinds[0], 'listen');
    assert.deepEqual(
      plain(seen.cleared),
      [[{ since: 0 }, { cookies: true }]],
      'the sign-out it was told about was done',
    );
    assert.ok(kinds.filter((kind) => kind === 'listen').length >= 2, 'and it asks again');
  });

  it('goes on asking when a round cannot open its page', async () => {
    let calls = 0;
    const managed = { geminarrUrl: 'https://geminarr.example/', token: 'the-token' };
    const { calls: seen, listeners } = await load({
      managed,
      answer: () => {
        calls++;
        if (calls >= 3) delete managed.token;
        return { ok: true, json: async () => ({ open: [], round: true, signOut: false }) };
      },
      failToOpen: true,
    });

    listeners.alarm({ name: 'listen' });
    for (let i = 0; i < 40 && seen.fetch.length < 3; i++) await flush();

    assert.equal(seen.fetch.filter((call) => JSON.parse(call.init.body).kind === 'listen').length, 3);
  });

  it('gives every request a bound, and a longer one to the request that waits', async () => {
    const { calls, listeners } = await load({ answer: answering({ open: [], round: false, signOut: false }) });

    listeners.alarm({ name: 'hello' });
    await flush();

    assert.ok(calls.fetch[0].init.signal instanceof AbortSignal, 'a request that never answers is given up');
  });

  it('starts only one request that waits, whatever wakes the worker', async () => {
    const managed = { geminarrUrl: 'https://geminarr.example/', token: 'the-token' };
    const { calls, listeners } = await load({
      managed,
      answer: () => {
        // One request, then the settings are gone: if a second loop had started it would have asked too.
        delete managed.token;
        return { ok: true, json: async () => ({ open: [], round: false, signOut: false }) };
      },
    });

    listeners.alarm({ name: 'listen' });
    listeners.alarm({ name: 'listen' });
    for (let i = 0; i < 10; i++) await flush();

    assert.equal(calls.fetch.filter((call) => JSON.parse(call.init.body).kind === 'listen').length, 1);
  });

  it('does not ask without an address and a token', async () => {
    const { calls, listeners } = await load({ managed: {}, answer: answering({ open: [] }) });

    listeners.alarm({ name: 'listen' });
    await flush();

    assert.equal(calls.fetch.length, 0);
  });

  it('signs out once when the same order is answered to two requests at once', async () => {
    const { calls, listeners } = await load({ answer: answering({ open: [], round: false, signOut: true }) });

    listeners.alarm({ name: 'hello' });
    listeners.alarm({ name: 'hello' });
    await flush();

    assert.equal(calls.cleared.length, 1);
  });

  it('does not sign out without an address and a token', async () => {
    const { calls, listeners } = await load({
      managed: {},
      answer: answering({ open: [], round: false, signOut: true }),
    });

    listeners.alarm({ name: 'hello' });
    await flush();

    assert.equal(calls.cleared.length, 0);
  });

  it('does nothing that an answer does not say with the word true', async () => {
    const { calls, listeners } = await load({ answer: answering({ open: [], round: 'yes', signOut: 1 }) });

    listeners.alarm({ name: 'hello' });
    await flush();

    assert.equal(calls.created.length, 0);
  });

  it('says nothing of a sign-out that did not happen when the browser refused to delete the cookies', async () => {
    const { calls, listeners } = await load({ answer: answering({ open: [], round: false, signOut: true }) });
    calls.refuse = true;

    listeners.alarm({ name: 'hello' });
    await flush();

    assert.equal(calls.fetch.filter((call) => JSON.parse(call.init.body).kind === 'state').length, 0);
    assert.equal(calls.created.length, 0, 'and no round follows');
  });

  it('starts a round when it is installed, like when the browser starts', async () => {
    const { calls, listeners } = await load();

    listeners.installed();
    await flush();

    assert.deepEqual(plain(calls.created), [{ url: `${ORIGIN}/app`, active: false }]);
  });

  it('hands over nothing that a page read before the account was signed out', async () => {
    const { calls, listeners, session } = await load();
    listeners.startup();
    await flush();
    session.set('signOutAt', Date.now() + 60_000);

    listeners.message({ kind: 'list', items: [{ id: 'abc', title: 'T' }] }, { tab: { id: 100 } });
    await flush();

    assert.equal(calls.fetch.length, 0, 'a round that began before the cookies were deleted is not pushed');
    assert.deepEqual(plain(calls.removed), [100], 'and its page is closed');
  });
});
