import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';
import vm from 'node:vm';

import { JSDOM } from 'jsdom';

const SOURCE = readFileSync(new URL('../../../extension/content.js', import.meta.url), 'utf8');
const ORIGIN = 'https://assistant.example';
const flush = () => new Promise((resolve) => setImmediate(resolve));

const SIGNED_IN = '<a href="https://accounts.example/SignOutOptions?x=1">Account</a>';
const SIGN_IN = '<a href="https://accounts.example/ServiceLogin?continue=x">Sign in</a>';
const list = (links) => '<conversations-list><infinite-scroller>' + links + '</infinite-scroller></conversations-list>';
const link = (id, title = id) => `<a href="/app/${id}">${title}</a>`;

/**
 * Runs the content script on a synthetic page, with waits that take no time. `onWait(n)` is called at each wait with its number, to let a page change while the script is looking at it.
 * What it sent to the background is returned.
 */
async function run(
  html,
  path,
  { onWait = () => {}, rejectSends = false, mine = true, probe = false, partAnswer = () => ({ taken: false }) } = {},
) {
  const dom = new JSDOM(html, { url: `${ORIGIN}${path}` });
  // jsdom does not implement innerText: what a page shows is its text, without what is not displayed.
  function shown(node) {
    if (node.nodeType === 3) return node.textContent;
    if (node.nodeType !== 1 || node.style.display === 'none') return '';
    return [...node.childNodes].map(shown).join('');
  }
  Object.defineProperty(dom.window.HTMLElement.prototype, 'innerText', {
    get() {
      return shown(this);
    },
  });
  const sent = [];
  let waits = 0;
  const chrome = {
    runtime: {
      sendMessage: (message) => {
        // The question that the script asks of the worker first: whether the page is one that the extension opened.
        if (message.kind === 'mine?') return Promise.resolve(mine);
        // Whether Geminarr holds this conversation whole, so that its end is enough.
        if (message.kind === 'probe?') return Promise.resolve(probe);
        sent.push(message);
        // The end of a conversation is answered by Geminarr: whether it joins what it holds.
        if (message.kind === 'part') return Promise.resolve(partAnswer(message));
        return rejectSends ? Promise.reject(new Error('Extension context invalidated.')) : Promise.resolve();
      },
    },
  };
  const context = vm.createContext({
    document: dom.window.document,
    location: dom.window.location,
    chrome,
    JSON,
    Promise,
    Date,
    setTimeout: (callback) => {
      onWait(++waits, dom.window.document);
      queueMicrotask(callback);
    },
  });
  // S1523 (code-eval), by design: this runs the extension's own file, read from this repository, in a context that holds nothing but a synthetic page.
  // eslint-disable-next-line sonarjs/code-eval -- the extension's own source, in a vm context with a synthetic page
  vm.runInContext(SOURCE, context); // NOSONAR
  await flush();
  // What the script built lives in another realm: a plain copy is what is compared.
  return { sent: JSON.parse(JSON.stringify(sent)), waits };
}

describe('which pages are read', () => {
  it('reads nothing on a page that a person opened, and does not even wait', async () => {
    const { sent, waits } = await run(SIGNED_IN + list(link('c1a2b3')), '/app', { mine: false });

    assert.deepEqual(sent, []);
    assert.equal(waits, 0);
  });

  it('reads nothing of a conversation that a person is reading', async () => {
    const { sent } = await run(
      '<user-query><user-query-content>Hello</user-query-content></user-query>',
      '/app/c1a2b3',
      { mine: false },
    );

    assert.deepEqual(sent, []);
  });
});

describe('the list page', () => {
  it('sends the conversations of the page of an account, once each, in the order of the page, with their titles', async () => {
    const links = [
      link('c1a2b3', 'One'),
      link('d2b3c4', 'Two'),
      link('c1a2b3', 'One again'),
      '<a href="/faq">Help</a>',
    ].join('');
    const { sent } = await run(SIGNED_IN + list(links), '/app');

    assert.deepEqual(sent, [
      {
        kind: 'list',
        items: [
          { id: 'c1a2b3', title: 'One' },
          { id: 'd2b3c4', title: 'Two' },
        ],
        complete: true,
        labels: [],
      },
    ]);
  });

  it('sends the label of the account button with the list, a few of them at most, and nothing when the page has none', async () => {
    const buttons = Array.from(
      { length: 6 },
      (_, index) =>
        `<a href="https://accounts.example/SignOutOptions?x=${index}" aria-label="Google Account: Jane Doe (jane@example.org)">A</a>`,
    ).join('');
    const { sent } = await run(buttons + list(link('c1a2b3')), '/app');

    assert.equal(sent[0].labels.length, 4);
    assert.equal(sent[0].labels[0], 'Google Account: Jane Doe (jane@example.org)');
  });

  it('sends an empty list for an account that has no conversation, and that says it is signed in', async () => {
    const { sent } = await run(`${SIGNED_IN}${list('')}`, '/app');

    assert.deepEqual(sent, [{ kind: 'list', items: [], complete: true, labels: [] }]);
  });

  it('does not take for a conversation a link whose id is longer than the one that Geminarr takes', async () => {
    const { sent } = await run(SIGNED_IN + list(link('a'.repeat(64)) + link('b'.repeat(65))), '/app');

    assert.deepEqual(
      sent[0].items.map((item) => item.id),
      ['a'.repeat(64)],
    );
  });

  it('says that nobody is signed in only when the page shows a sign-in link, and sends no list', async () => {
    const { sent } = await run(`${SIGN_IN}${list(link('c1a2b3'))}`, '/app');

    assert.deepEqual(sent, [{ kind: 'state', signedIn: false }]);
  });

  it('believes a sign-out link that comes late rather than a sign-in link that was there first', async () => {
    const { sent } = await run(`${SIGN_IN}${list(link('c1a2b3'))}`, '/app', {
      onWait: (n, document) => {
        if (n === 5) document.body.insertAdjacentHTML('afterbegin', SIGNED_IN);
      },
    });

    assert.equal(sent[0].kind, 'list');
  });

  it('does not believe a sign-in link before it has been there for a while', async () => {
    const { sent, waits } = await run(`${SIGN_IN}${list(link('c1a2b3'))}`, '/app');

    assert.deepEqual(sent, [{ kind: 'state', signedIn: false }]);
    assert.ok(
      waits >= 10,
      'five seconds at least, so that a page that is still loading is not taken for a page nobody signed in to',
    );
  });

  it('says that the page is not what it knows, and not that nobody is signed in, when it shows neither link', async () => {
    const { sent, waits } = await run(`<p>Something else entirely</p>${list(link('c1a2b3'))}`, '/app');

    assert.deepEqual(sent, [{ kind: 'broken', step: 'page' }]);
    assert.ok(waits >= 10, 'it asked for a while before it said so');
  });

  it('says that the list is not what it knows when a page of an account has no list to scroll', async () => {
    const { sent, waits } = await run(`${SIGNED_IN}<p>No list here</p>`, '/app');

    assert.deepEqual(sent, [{ kind: 'broken', step: 'list' }]);
    assert.ok(waits >= 10, 'it asked for a while before it said so');
  });

  it('waits for the list to show, a little after the account does', async () => {
    const { sent } = await run(SIGNED_IN, '/app', {
      onWait: (n, document) => {
        if (n === 5) document.body.insertAdjacentHTML('beforeend', list(link('c1a2b3')));
      },
    });

    assert.equal(sent[0].kind, 'list');
    assert.equal(sent[0].items.length, 1);
  });

  it('scrolls the list until it stops growing', async () => {
    const { sent } = await run(`${SIGNED_IN}${list(link('c1a2b3'))}`, '/app', {
      onWait: (n, document) => {
        if (n <= 2) document.querySelector('infinite-scroller').insertAdjacentHTML('beforeend', link(`more${n}`));
      },
    });

    assert.equal(sent[0].items.length, 3);
    assert.equal(sent[0].complete, true, 'a scroll that brought nothing more is the end of the list');
  });

  it('says that a list is not complete when the scrolls ran out before its end', async () => {
    // Every scroll brings one more conversation: the list never ends within the scrolls that are allowed.
    const { sent } = await run(`${SIGNED_IN}${list(link('c1a2b3'))}`, '/app', {
      onWait: (n, document) =>
        document.querySelector('infinite-scroller').insertAdjacentHTML('beforeend', link(`more${n}`)),
    });

    assert.equal(sent[0].complete, false);
  });

  it('takes a list of exactly what Geminarr takes whole, and says that it is complete', async () => {
    const exact = Array.from({ length: 500 }, (_, i) => link(`c${i}`)).join('');
    const { sent } = await run(SIGNED_IN + list(exact), '/app');

    assert.equal(sent[0].items.length, 500);
    assert.equal(sent[0].complete, true);
  });

  it('cuts a list at what Geminarr takes, the most recent first, and says that it is not complete', async () => {
    const many = Array.from({ length: 501 }, (_, i) => link(`c${i}`)).join('');
    const { sent } = await run(SIGNED_IN + list(many), '/app');

    assert.equal(sent[0].items.length, 500);
    assert.equal(sent[0].items[0].id, 'c0');
    assert.equal(sent[0].complete, false);
  });

  it('does not make an error of a message that finds nobody to read it', async () => {
    const { sent } = await run(`${SIGNED_IN}${list(link('c1a2b3'))}`, '/app', { rejectSends: true });

    assert.equal(sent.length, 1);
  });
});

describe('the page of a conversation', () => {
  const turns =
    '<user-query><user-query-content>Hello</user-query-content></user-query><model-response><message-content>Hi there</message-content></model-response><model-response> </model-response>';

  it('sends the turns that it shows, by role, once what it shows has stopped changing', async () => {
    const { sent } = await run(`<title>A title</title>${turns}`, '/app/c1a2b3');

    assert.deepEqual(sent, [
      {
        kind: 'conversation',
        id: 'c1a2b3',
        title: 'A title',
        turns: [
          { role: 'user', text: 'Hello' },
          { role: 'assistant', text: 'Hi there' },
        ],
      },
    ]);
  });

  it('leaves out what the page says to a screen reader only, and puts back what it hid', async () => {
    const page =
      '<user-query><user-query-content><span class="cdk-visually-hidden">You said</span><p>Hello there</p></user-query-content></user-query>' +
      '<model-response><message-content>Hi</message-content></model-response>';
    const { sent } = await run(page, '/app/c1a2b3');

    assert.deepEqual(sent[0].turns, [
      { role: 'user', text: 'Hello there' },
      { role: 'assistant', text: 'Hi' },
    ]);
  });

  describe('a conversation that the page keeps only in part', () => {
    // A chat of a height of 20 per turn seen through a screen of 40 (a step of 28), that shows two turns at a time: the turn at the top of the screen is the one of the scroll position.
    const conversation = (count) =>
      Array.from({ length: count }, (_, i) => [i % 2 === 0 ? 'user' : 'assistant', `Turn ${i}`]);
    const render = (document, all, first) => {
      document.getElementById('chat').innerHTML = all
        .slice(first, first + 2)
        .map(([role, text]) =>
          role === 'user'
            ? `<user-query><user-query-content>${text}</user-query-content></user-query>`
            : `<model-response><message-content>${text}</message-content></model-response>`,
        )
        .join('');
    };
    const chat = (document, count, { canScroll = true } = {}) => {
      const all = conversation(count);
      const element = document.getElementById('chat');
      element.setAttribute('style', 'overflow-y: auto');
      Object.defineProperty(element, 'scrollHeight', { value: count * 20 });
      Object.defineProperty(element, 'clientHeight', { value: 40 });
      let top = count * 20;
      Object.defineProperty(element, 'scrollTop', {
        get: () => top,
        set: (value) => {
          if (canScroll) top = value;
        },
      });
      return () => render(document, all, Math.min(count - 2, Math.floor(top / 20)));
    };
    const readIt = async (count, { canScroll, ...options } = {}) => {
      let show;
      return run('<div id="chat"></div>', '/app/c1a2b3', {
        ...options,
        onWait: (n, document) => {
          show ??= chat(document, count, { canScroll });
          show();
        },
      });
    };

    it('reads it from its end to its beginning and hands over every turn once, in order', async () => {
      const { sent } = await readIt(6);

      assert.deepEqual(
        sent[0].turns.map((turn) => turn.text),
        ['Turn 0', 'Turn 1', 'Turn 2', 'Turn 3', 'Turn 4', 'Turn 5'],
      );
      assert.deepEqual(
        sent[0].turns.map((turn) => turn.role),
        ['user', 'assistant', 'user', 'assistant', 'user', 'assistant'],
      );
    });

    it('reads a very long one whole: its length is not a reason to give it up', async () => {
      const { sent } = await readIt(1500);

      assert.equal(sent[0].turns.length, 1500);
      assert.equal(sent[0].turns[0].text, 'Turn 0');
      assert.equal(sent[0].turns[1499].text, 'Turn 1499');
    });

    describe('one that Geminarr holds whole', () => {
      const last = (count, n) => Array.from({ length: n }, (_, i) => `Turn ${count - n + i}`);

      it('hands over the end of it only, and is done, when Geminarr says that it joins what it holds', async () => {
        const full = await readIt(300);
        const { sent, waits } = await readIt(300, { probe: true, partAnswer: () => ({ taken: true }) });

        assert.deepEqual(
          sent.map((message) => message.kind),
          ['part', 'done'],
        );
        assert.deepEqual(
          sent[0].turns.map((turn) => turn.text),
          last(300, sent[0].turns.length),
          'the last turns, in order',
        );
        assert.ok(sent[0].turns.length >= 10);
        assert.ok(
          waits * 10 < full.waits,
          `the end is read in a few steps (${waits}), the whole conversation in many (${full.waits})`,
        );
      });

      it('goes on to the whole conversation, from where the end was read, when the end does not join', async () => {
        const { sent } = await readIt(60, { probe: true, partAnswer: () => ({ taken: false }) });

        assert.deepEqual(
          sent.map((message) => message.kind),
          ['part', 'conversation'],
        );
        assert.deepEqual(
          sent[1].turns.map((turn) => turn.text),
          Array.from({ length: 60 }, (_, i) => `Turn ${i}`),
          'every turn once, in order: the end that was read is not read again',
        );
      });

      it('hands over a short conversation whole, without a part, when its beginning is reached on the way', async () => {
        const { sent } = await readIt(6, { probe: true });

        assert.deepEqual(
          sent.map((message) => message.kind),
          ['conversation'],
        );
        assert.equal(sent[0].turns.length, 6);
      });

      it('gives up when the page does not follow the scroll', async () => {
        const { sent } = await readIt(60, { probe: true, canScroll: false });

        assert.deepEqual(sent, [{ kind: 'giveup' }]);
      });
    });

    it('reads a conversation that Geminarr does not hold whole, with no part', async () => {
      const { sent } = await readIt(60);

      assert.deepEqual(
        sent.map((message) => message.kind),
        ['conversation'],
      );
    });

    it('hands over nothing, and gives up, when the page does not follow the scroll', async () => {
      const { sent } = await readIt(6, { canScroll: false });

      assert.deepEqual(sent, [{ kind: 'giveup' }]);
    });
  });

  it('says which conversation shows no turn at all, and leaves it to Geminarr to tell a page that changed from a conversation with nothing to read', async () => {
    const { sent } = await run('<p>Nothing like a conversation</p>', '/app/c1a2b3');

    assert.deepEqual(sent, [{ kind: 'broken', step: 'turns', id: 'c1a2b3' }]);
  });

  it('hands over nothing of a conversation that never stops changing, and gives up so that its page is closed', async () => {
    const { sent } = await run(turns, '/app/c1a2b3', {
      onWait: (n, document) => {
        document.querySelector('message-content').textContent = `Hi there ${n}`;
      },
    });

    assert.deepEqual(sent, [{ kind: 'giveup' }]);
  });

  it('reads nothing on a page that is not the list or a conversation', async () => {
    const { sent } = await run(`${SIGNED_IN}${turns}`, '/faq');

    assert.deepEqual(sent, []);
  });
});
