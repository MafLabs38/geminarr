// Reads the page of the assistant as a person sees it: whether somebody is signed in, the list of conversations, the turns of one conversation. It sends what it read to background.js and nothing else:
// it submits no prompt, clicks nothing but scrolls the list, and reads no other page. If the page changes shape, only these selectors change, and the extension says so (kind "broken", with the step) instead of
// guessing: a page that nobody is signed in to is told by what it shows (a sign-in link), never by the absence of the sign-out one.
const SELECTORS = {
  signedIn: 'a[href*="SignOutOptions"]',
  signedOut: 'a[href*="ServiceLogin"]',
  link: 'a[href]',
  scroller: 'conversations-list infinite-scroller, infinite-scroller',
  turn: 'user-query, model-response',
  turnText: 'user-query-content, message-content',
};
// The same bound as Geminarr's (an id of a conversation is at most 64 characters): a longer one is not a conversation of this page.
const ID_IN_LINK = /\/app\/([A-Za-z0-9_-]{1,64})$/;
const SCROLL_MS = 1500;
const MAX_SCROLLS = 30;
const STABLE_ROUNDS = 6;
const POLL_MS = 500;
const MAX_POLLS = 40;
// The page takes time to say who is signed in: it is asked about for up to 30 seconds, and a sign-in link must have been there for 5 before it is believed.
const MAX_PAGE_POLLS = 60;
const SIGNED_OUT_POLLS = 10;

const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
// The worker may have been restarted or the extension reloaded: a message that finds nobody is a read that is lost, not an error on the page.
const send = (message) => chrome.runtime.sendMessage(message).catch(() => {});

function conversationLinks() {
  return [...document.querySelectorAll(SELECTORS.link)]
    .map((link) => ({ link, match: ID_IN_LINK.exec(link.getAttribute('href') ?? '') }))
    .filter(({ match }) => match);
}

/** "in", "out" or "unknown": what the page shows after it had the time to show it. */
async function whoIsSignedIn() {
  for (let i = 0; i < MAX_PAGE_POLLS; i++) {
    if (document.querySelector(SELECTORS.signedIn)) return 'in';
    if (i >= SIGNED_OUT_POLLS && document.querySelector(SELECTORS.signedOut)) return 'out';
    await wait(POLL_MS);
  }
  return 'unknown';
}

/** The list that scrolls, or nothing: it shows a little after the account does (seen on the real page), so it is asked for as long as the account was. */
async function listThatScrolls() {
  for (let i = 0; i < MAX_PAGE_POLLS; i++) {
    const found = document.querySelector(SELECTORS.scroller);
    if (found) return found;
    await wait(POLL_MS);
  }
  return null;
}

async function readList() {
  const who = await whoIsSignedIn();
  // One word per page: a page that nobody is signed in to says so and has no list; a page of an account sends its list, which says as much; a page that is neither is not what this knows.
  if (who === 'unknown') return send({ kind: 'broken', step: 'page' });
  if (who === 'out') return send({ kind: 'state', signedIn: false });
  const scroller = await listThatScrolls();
  if (!scroller) return send({ kind: 'broken', step: 'list' });
  let seen = -1;
  for (let i = 0; i < MAX_SCROLLS && conversationLinks().length !== seen; i++) {
    seen = conversationLinks().length;
    scroller.scrollTop = scroller.scrollHeight;
    await wait(SCROLL_MS);
  }
  const items = conversationLinks().map(({ link, match }) => ({ id: match[1], title: (link.innerText ?? '').trim() }));
  // The same conversation can be linked twice on a page: the first place is its place.
  return send({
    kind: 'list',
    items: items.filter((item, index) => items.findIndex((other) => other.id === item.id) === index),
  });
}

function readTurns() {
  return [...document.querySelectorAll(SELECTORS.turn)]
    .map((element) => ({
      role: element.tagName === 'USER-QUERY' ? 'user' : 'assistant',
      text: (element.querySelector(SELECTORS.turnText) ?? element).innerText.trim(),
    }))
    .filter((turn) => turn.text);
}

// A conversation is read when what it shows has stopped changing: the page fills in as it loads (a partial answer is never handed over).
async function readConversation(id) {
  let last = '';
  let stable = 0;
  for (let i = 0; i < MAX_POLLS; i++) {
    await wait(POLL_MS);
    const turns = readTurns();
    const snapshot = JSON.stringify(turns);
    stable = turns.length > 0 && snapshot === last ? stable + 1 : 0;
    last = snapshot;
    if (stable >= STABLE_ROUNDS) return send({ kind: 'conversation', id, title: document.title, turns });
  }
  // One that shows nothing is a conversation with nothing to read (or, if several do, a page that changed: Geminarr counts); one that never stops changing is read at the next round, and its page is closed now.
  return send(last === '[]' ? { kind: 'broken', step: 'turns', id } : { kind: 'giveup' });
}

// Only the pages that the worker opened are read: a page that a person opened is theirs, and is neither scrolled nor read.
async function start() {
  const mine = await chrome.runtime.sendMessage({ kind: 'mine?' }).catch(() => false);
  if (!mine) return;
  const conversation = ID_IN_LINK.exec(location.pathname);
  if (conversation) await readConversation(conversation[1]);
  else if (location.pathname === '/app') await readList();
}

void start();
