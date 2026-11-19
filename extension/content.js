// Reads the page of the assistant as a person sees it: whether somebody is signed in, the list of conversations, the turns of one conversation. It sends what it read to background.js and nothing else:
// it submits no prompt, clicks nothing but scrolls the list, and reads no other page. If the page changes shape, only these selectors change, and the extension says so (kind "broken", with the step) instead of
// guessing: a page that nobody is signed in to is told by what it shows (a sign-in link), never by the absence of the sign-out one.
const SELECTORS = {
  signedIn: 'a[href*="SignOutOptions"]',
  // The label of the account button says who is signed in ("Google Account: Jane Doe (jane@example.org)"): Geminarr reads it, and it is shown to the operator only.
  accountLabel: 'a[href*="SignOutOptions"][aria-label]',
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
// The most conversations that Geminarr takes in one list.
const MAX_ITEMS = 500;
const STABLE_ROUNDS = 6;
// A conversation is read from its end to its beginning, a window at a time: each step is this share of the height of the screen (the windows overlap), the page gets this long to show what it brings in, and the top must stay
// the top for a few looks. A conversation that takes more steps than this is not handed over (a tab of a hidden page may wait a second at each step).
const CHAT_STEP = 0.7;
const CHAT_SCROLL_MS = 300;
const CHAT_STEADY_LOOKS = 3;
const MAX_CHAT_STEPS = 200;
const POLL_MS = 500;
const MAX_POLLS = 40;
// The page takes time to say who is signed in: it is asked about for up to 30 seconds, and a sign-in link must have been there for 5 before it is believed.
const MAX_PAGE_POLLS = 60;
const SIGNED_OUT_POLLS = 10;

// At most this many labels are sent: the page links its account button more than once, and Geminarr takes no more than this many.
const MAX_LABELS = 4;

const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
// The worker may have been restarted or the extension reloaded: a message that finds nobody is a read that is lost, not an error on the page.
const send = (message) => chrome.runtime.sendMessage(message).catch(() => {});

function accountLabels() {
  return [...document.querySelectorAll(SELECTORS.accountLabel)]
    .map((element) => element.getAttribute('aria-label') ?? '')
    .filter(Boolean)
    .slice(0, MAX_LABELS);
}

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
  // The end of the list is when a scroll brings nothing more: a list that was cut because the scrolls ran out is not the whole history, and Geminarr is told which it is.
  let complete = false;
  for (let i = 0; i < MAX_SCROLLS && !complete; i++) {
    const seen = conversationLinks().length;
    scroller.scrollTop = scroller.scrollHeight;
    await wait(SCROLL_MS);
    complete = conversationLinks().length === seen;
  }
  const items = conversationLinks().map(({ link, match }) => ({ id: match[1], title: (link.innerText ?? '').trim() }));
  // The same conversation can be linked twice on a page: the first place is its place.
  const unique = items.filter((item, index) => items.findIndex((other) => other.id === item.id) === index);
  // Geminarr takes no more than MAX_ITEMS: a longer list is cut here (the most recent first), and is then not complete.
  return send({
    kind: 'list',
    items: unique.slice(0, MAX_ITEMS),
    complete: complete && unique.length <= MAX_ITEMS,
    labels: accountLabels(),
  });
}

// What the page says to a screen reader only (the label "You said", a summary of the message): it is not what was said, and it would come first in the text of a turn.
const SCREEN_READER_ONLY = /visually-hidden|sr-only/;

/** The text of an element as a person sees it: what the page shows to a screen reader only is left out (it is hidden for the length of the read, and shown again). */
function visibleText(element) {
  const hidden = [...element.querySelectorAll('*')].filter((node) =>
    SCREEN_READER_ONLY.test(node.getAttribute('class') ?? ''),
  );
  const before = hidden.map((node) => node.style.display);
  for (const node of hidden) node.style.setProperty('display', 'none');
  try {
    return element.innerText.trim();
  } finally {
    hidden.forEach((node, index) => node.style.setProperty('display', before[index]));
  }
}

function readTurns() {
  return [...document.querySelectorAll(SELECTORS.turn)]
    .map((element) => ({
      role: element.tagName === 'USER-QUERY' ? 'user' : 'assistant',
      text: visibleText(element.querySelector(SELECTORS.turnText) ?? element),
    }))
    .filter((turn) => turn.text);
}

const sameTurn = (a, b) => a.role === b.role && a.text === b.text;

/** The turns that the page showed earlier in the conversation, put before the ones that were already read: the two windows overlap by the turns that both showed. */
function joinEarlier(earlier, later) {
  const most = Math.min(earlier.length, later.length);
  for (let overlap = most; overlap > 0; overlap--) {
    if (earlier.slice(-overlap).every((turn, index) => sameTurn(turn, later[index])))
      return [...earlier.slice(0, -overlap), ...later];
  }
  return [...earlier, ...later];
}

/** The element that scrolls the conversation: the first ancestor of a turn that has more to show than its height. */
function chatScroller() {
  for (let node = document.querySelector(SELECTORS.turn)?.parentElement; node; node = node.parentElement) {
    if (
      node.scrollHeight > node.clientHeight + 1 &&
      /auto|scroll/.test(document.defaultView.getComputedStyle(node).overflowY)
    )
      return node;
  }
  return null;
}

/**
 * The page keeps in the document only the turns near the screen, and brings the older ones in as the conversation is scrolled up: what is shown when the page has loaded is the end of the conversation.
 * The whole of it is read from the end to the beginning, a window at a time (each overlapping the last), and handed over only once the beginning was reached: a conversation without its beginning would put
 * turns at the wrong positions in Mnemarr. Gives nothing (null) when the beginning could not be reached.
 */
async function readWholeConversation() {
  let turns = readTurns();
  const scroller = chatScroller();
  if (!scroller) return turns;
  let steady = 0;
  for (let step = 0; step < MAX_CHAT_STEPS; step++) {
    const height = scroller.scrollHeight;
    scroller.scrollTop = Math.max(0, scroller.scrollTop - Math.floor(scroller.clientHeight * CHAT_STEP));
    await wait(CHAT_SCROLL_MS);
    turns = joinEarlier(readTurns(), turns);
    // At the top, older turns may still come in: the end is a top that stays the top for a few looks.
    steady = scroller.scrollTop === 0 && scroller.scrollHeight === height ? steady + 1 : 0;
    if (steady >= CHAT_STEADY_LOOKS) return turns;
  }
  return null;
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
    if (stable >= STABLE_ROUNDS) {
      const whole = await readWholeConversation();
      // The beginning could not be reached: nothing is handed over, the page is closed, and the next round tries again.
      return whole ? send({ kind: 'conversation', id, title: document.title, turns: whole }) : send({ kind: 'giveup' });
    }
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

// A content script is not a module: it cannot await at the top level (S7785), so the start is not awaited.
void start(); // NOSONAR
