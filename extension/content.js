// Reads the page of the assistant as a person sees it: whether somebody is signed in, the list of conversations, the turns of one conversation. It sends what it read to background.js and nothing else:
// it submits no prompt, clicks nothing but scrolls the list, and reads no other page. If the page changes shape, only these selectors change.
const SELECTORS = {
  signedIn: 'a[href*="SignOutOptions"]',
  link: 'a[href]',
  scroller: 'conversations-list infinite-scroller, infinite-scroller',
  turn: 'user-query, model-response',
  turnText: 'user-query-content, message-content',
};
const ID_IN_LINK = /\/app\/([A-Za-z0-9_-]+)$/;
const SETTLE_MS = 5000;
const SCROLL_MS = 1500;
const MAX_SCROLLS = 30;
const STABLE_ROUNDS = 6;
const POLL_MS = 500;
const MAX_POLLS = 40;

const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const send = (message) => chrome.runtime.sendMessage(message);

function conversationLinks() {
  return [...document.querySelectorAll(SELECTORS.link)]
    .map((link) => ({ link, match: ID_IN_LINK.exec(link.getAttribute('href') ?? '') }))
    .filter(({ match }) => match);
}

async function readList() {
  await wait(SETTLE_MS);
  // One word per page: a page that nobody is signed in to says so and has no list; a page of an account sends its list, which says as much.
  if (!document.querySelector(SELECTORS.signedIn)) {
    send({ kind: 'state', signedIn: false });
    return;
  }
  const scroller = document.querySelector(SELECTORS.scroller);
  let seen = -1;
  for (let i = 0; i < MAX_SCROLLS && scroller && conversationLinks().length !== seen; i++) {
    seen = conversationLinks().length;
    scroller.scrollTop = scroller.scrollHeight;
    await wait(SCROLL_MS);
  }
  const items = conversationLinks().map(({ link, match }) => ({ id: match[1], title: (link.innerText ?? '').trim() }));
  // The same conversation can be linked twice on a page: the first place is its place.
  send({
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

// A conversation is read when what it shows has stopped changing: the page fills in as it loads.
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
      send({ kind: 'conversation', id, title: document.title, turns });
      return;
    }
  }
}

const conversation = ID_IN_LINK.exec(location.pathname);
if (conversation) void readConversation(conversation[1]);
else if (location.pathname === '/app') void readList();
