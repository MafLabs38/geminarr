// The only place that talks to Geminarr, and the one that opens the pages of the assistant. Its address and its token come from the policy of the browser (managed storage), never from the code,
// and it only ever sends to that address. A page of the assistant is opened in the background, read by content.js, and closed.
const ORIGIN = '__ASSISTANT_ORIGIN__';
const SWEEP_ALARM = 'sweep';
const UPDATE_ALARM = 'update';
// The extension says that it is there every half minute (the least that an alarm allows): Geminarr answers whether to look now (the interval of the rounds is over, a person is signing in, the operator asked) and whether to sign the account out. It is the only clock of the rounds, apart from the one at the browser's start or the extension's install, which looks once.
const HELLO_ALARM = 'hello';
// The alarm that starts the request that waits for an order, when the worker was stopped and the request with it.
const LISTEN_ALARM = 'listen';
const HELLO_MINUTES = 0.5;
// Two loads of a page of the assistant closer than this are told to Geminarr once.
const PAGE_EVENT_EVERY_MS = 5000;
// A request that Geminarr holds until the operator asks something (a sign-out, a round): an order reaches the browser at once, not at the next heartbeat. When it comes back at once with nothing to do, or does not come back, the next one waits this long.
const LISTEN_PAUSE_MS = 2000;
// A request that is answered within this long, or is given up (Geminarr holds a request that waits for 25 seconds at most).
const PUSH_TIMEOUT_MS = 30 * 1000;
const LISTEN_TIMEOUT_MS = 40 * 1000;
// When Geminarr cannot be reached, or refuses the request (another one already waits), the next one comes after this long.
const LISTEN_RETRY_MS = 30 * 1000;
const SIGN_OUT_KEY = 'signOutAt';
// The sign-out is not started again for this long (the same order, answered twice by two requests at once); a new *Disconnect* of the operator a moment later is done. Geminarr keeps asking until it hears that nobody is signed in.
const SIGN_OUT_EVERY_MS = 10 * 1000;
const WORK_TAB_PREFIX = 'workTab:';
// A page that has not answered after this long is closed: a hung tab must not pile up.
const TAB_LIFETIME_MS = 5 * 60 * 1000;
// At most this many conversations are opened for one answer, whatever the answer says: a tab is a page of the browser that holds the session.
// (The recent ones that changed, and the few older ones that the backfill brings, SPEC §6.3.)
// (The same number as `ExtensionService.MaxOpenPerAnswer`: Geminarr asks for no more.)
const MAX_TABS_PER_ANSWER = 25;
// The pages of one answer are opened one after the other, a pause of 1 to 4 seconds between two (SPEC §6.4): a person does not open twenty conversations at the same instant.
// The size of the window of a conversation: a screen that shows a few turns (the page brings in the turns around what is shown).
const CONVERSATION_WINDOW = { width: 1000, height: 800 };
const OPEN_PAUSE_MIN_MS = 1000;
const OPEN_PAUSE_SPREAD_MS = 3000;
// Not a secret, only a spread of the pauses: the browser's own source of randomness is used, so that nothing here is a generator to be careful with.
const randomUnit = () => crypto.getRandomValues(new Uint32Array(1))[0] / 2 ** 32;
const pauseBetweenOpens = () =>
  new Promise((resolve) => setTimeout(resolve, OPEN_PAUSE_MIN_MS + randomUnit() * OPEN_PAUSE_SPREAD_MS));

async function settings() {
  const { geminarrUrl, token } = await chrome.storage.managed.get(['geminarrUrl', 'token']);
  if (!geminarrUrl || !token) return null;
  let base = String(geminarrUrl);
  while (base.endsWith('/')) base = base.slice(0, -1);
  return { base, token };
}

/** Sends what the page showed, and gives back what Geminarr answered (a list of conversations to read), or nothing when it did not take it. */
async function push(body, timeoutMs = PUSH_TIMEOUT_MS) {
  const config = await settings();
  if (!config) return null;
  try {
    const response = await fetch(`${config.base}/api/v1/extension/push`, {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        authorization: `Bearer ${config.token}`,
        'x-geminarr-request': '1',
      },
      body: JSON.stringify({ ...body, version: chrome.runtime.getManifest().version }),
      // Every wait has a bound: a network that never answers is a failure, not a request that waits for ever.
      signal: AbortSignal.timeout(timeoutMs),
    });
    return response.ok ? await response.json() : null;
  } catch {
    return null;
  }
}

// One key for each tab that this extension opened: the writes of two tabs never overwrite each other's, as one object holding them all would.
const workTabKey = (tabId) => `${WORK_TAB_PREFIX}${tabId}`;

async function isWorkTab(tabId) {
  return workTabKey(tabId) in (await chrome.storage.session.get(workTabKey(tabId)));
}

async function openInBackground(path) {
  const tab = await chrome.tabs.create({ url: `${ORIGIN}${path}`, active: false });
  await chrome.storage.session.set({ [workTabKey(tab.id)]: Date.now() });
}

/**
 * A conversation is read in a window of its own, not focused. A page of a tab that is in the background is not shown, and the assistant brings in the older turns of a conversation only for a page that is shown: read in
 * a tab that nobody sees, a conversation comes without its beginning. A window is shown, and the tab that a person looks at (signing in, for one) is left alone.
 */
async function openConversation(id) {
  const opened = await chrome.windows.create({
    url: `${ORIGIN}/app/${id}`,
    focused: false,
    width: CONVERSATION_WINDOW.width,
    height: CONVERSATION_WINDOW.height,
  });
  const tab = opened.tabs?.[0];
  if (tab) await chrome.storage.session.set({ [workTabKey(tab.id)]: Date.now() });
}

async function closeTab(tabId) {
  await chrome.storage.session.remove(workTabKey(tabId));
  await chrome.tabs.remove(tabId).catch(() => {});
}

async function hasWorkTab() {
  return Object.keys(await chrome.storage.session.get(null)).some((key) => key.startsWith(WORK_TAB_PREFIX));
}

/** A round: one page of the list. What it finds, and what Geminarr asks for, is done by the messages below. */
async function openTheList() {
  try {
    // A round that finds the last one still reading (a slow page) waits: the sweep closes what hangs.
    if ((await settings()) && !(await hasWorkTab())) await openInBackground('/app');
  } catch {
    // The browser could not open the page: the next round asks again. A failed round never stops the request that waits for the orders of Geminarr.
  }
}

// One at a time: the alarm, the start of the browser and the install can wake the worker together, and two rounds that look before either has opened its tab would open two.
let roundsInTurn = Promise.resolve();
function round() {
  roundsInTurn = roundsInTurn.then(openTheList, openTheList);
  return roundsInTurn;
}

/**
 * Signs the account out, when the operator disconnected: the cookies of the browser are cleared (the permission `browsingData` only deletes, it reads nothing), which ends the session of the dedicated account (this
 * browser holds no other), and a round follows to see what the page shows. A sign-out page of the account provider cannot do it: it asks for a click, and this extension clicks nothing.
 */
// One at a time, decided before the first await: the request that waits and the heartbeat can both be told the same order together.
let signingOut = false;
async function signOut() {
  if (signingOut) return;
  signingOut = true;
  try {
    await doSignOut();
  } finally {
    signingOut = false;
  }
}

async function doSignOut() {
  if (!(await settings())) return;
  const { [SIGN_OUT_KEY]: last } = await chrome.storage.session.get(SIGN_OUT_KEY);
  if (typeof last === 'number' && Date.now() - last < SIGN_OUT_EVERY_MS) return;
  await chrome.storage.session.set({ [SIGN_OUT_KEY]: Date.now() });
  try {
    await chrome.browsingData.remove({ since: 0 }, { cookies: true });
  } catch {
    // The browser refused: Geminarr keeps asking, and the next time is in a few seconds.
    return;
  }
  // The pages of the assistant that a person has open still show the account that was signed in: they are loaded again, so that the desktop shows a page that is signed out (and the sign-in that follows is seen).
  await reloadAssistantTabs();
  // The cookies are gone: nobody is signed in, whatever the page shows next (this does not depend on a selector of the page). A round follows, to see what it shows.
  await push({ kind: 'state', signedIn: false });
  await round();
}

/** Loads again the tabs of the assistant that are open (a host permission is all that finding them takes): what they show comes from before the cookies were deleted. */
async function reloadAssistantTabs() {
  try {
    for (const tab of await chrome.tabs.query({ url: `${ORIGIN}/*` })) {
      if (typeof tab.id === 'number') await chrome.tabs.reload(tab.id);
    }
  } catch {
    // A tab that is gone, or a browser that refuses: the round that follows says what the page shows.
  }
}

/** What Geminarr asked for in its answer: a round of reading now, and the sign-out. Nothing else in an answer is acted on. */
async function actOn(answer) {
  if (answer?.signOut === true) await signOut();
  if (answer?.round === true) await round();
}

let lastPageEvent = 0;
/** A page of the assistant that a person has open has just loaded (a sign-in ends on one): Geminarr is told at once, not at the next half minute, and may ask for a round. The pages that this extension opened do not count. */
async function pageLoaded(tabId) {
  if ((await isWorkTab(tabId)) || Date.now() - lastPageEvent < PAGE_EVENT_EVERY_MS) return;
  lastPageEvent = Date.now();
  await actOn(await push({ kind: 'page' }));
}

let listening = false;
/**
 * Keeps one request open to Geminarr, which holds it (about 25 seconds) until the operator asks something of the extension, and does what it answers: so a *Disconnect* or a *Sign in* is acted on in the second, and the
 * sign-out is over before a person looks at the desktop. A request that comes back, empty or not, starts the next one; an alarm starts the loop again when the worker was stopped.
 */
async function listen() {
  if (listening) return;
  listening = true;
  try {
    while (await settings()) {
      const began = Date.now();
      const answer = await push({ kind: 'listen' }, LISTEN_TIMEOUT_MS);
      try {
        await actOn(answer);
      } catch {
        // What was asked could not be done (the browser refused): the loop goes on, and Geminarr asks again.
      }
      const ordered = answer?.round === true || answer?.signOut === true;
      let pause = 0;
      if (answer === null) pause = LISTEN_RETRY_MS;
      else if (!ordered && Date.now() - began < LISTEN_PAUSE_MS) pause = LISTEN_PAUSE_MS;
      if (pause > 0) await new Promise((resolve) => setTimeout(resolve, pause));
    }
  } finally {
    listening = false;
  }
}

/** The extension says that it is there, and does what it is asked. */
async function hello() {
  await actOn(await push({ kind: 'hello' }));
}

async function sweep() {
  const everything = await chrome.storage.session.get(null);
  for (const [key, since] of Object.entries(everything)) {
    if (key.startsWith(WORK_TAB_PREFIX) && Date.now() - since > TAB_LIFETIME_MS)
      await closeTab(Number(key.slice(WORK_TAB_PREFIX.length)));
  }
}

/** Whether the page was opened before the cookies were deleted: what it read is the session that was just ended, and is not handed over. */
async function startedBeforeSignOut(tabId) {
  const key = workTabKey(tabId);
  const { [SIGN_OUT_KEY]: signedOutAt, [key]: startedAt } = await chrome.storage.session.get([SIGN_OUT_KEY, key]);
  return typeof signedOutAt === 'number' && typeof startedAt === 'number' && startedAt < signedOutAt;
}

async function onMessage(message, sender) {
  try {
    // A page that gives up has nothing to hand over: it is closed, and Geminarr is told nothing. Neither has a page that was read before the account was signed out.
    const stale = sender.tab ? await startedBeforeSignOut(sender.tab.id) : false;
    const answer = message.kind === 'giveup' || stale ? null : await push(message);
    if (message.kind === 'list') {
      // What Geminarr answers is a list of ids; anything else asks for nothing.
      const ids = (Array.isArray(answer?.open) ? answer.open : [])
        .filter((candidate) => typeof candidate === 'string' && /^[A-Za-z0-9_-]{1,64}$/.test(candidate))
        .slice(0, MAX_TABS_PER_ANSWER);
      for (const [index, id] of ids.entries()) {
        if (index > 0) await pauseBetweenOpens();
        await openConversation(id);
      }
    }
    // A word of the page may be answered by a request too (a person is signing in, the operator disconnected): it is done after the page has been dealt with.
    await actOn(answer);
  } catch {
    // The browser could not open a page (or keep what it opened): the next round asks again.
  } finally {
    // A page that this extension opened is done once it has said what it had to say. A page that a person opened is theirs, and stays.
    if (sender.tab && (await isWorkTab(sender.tab.id))) await closeTab(sender.tab.id);
  }
}

chrome.runtime.onMessage.addListener((message, sender, respond) => {
  // A page of the assistant asks whether this extension opened it: only the pages that it opened are read.
  if (message.kind === 'mine?') {
    void isWorkTab(sender.tab?.id).then(respond);
    return true;
  }

  void onMessage(message, sender);
  return false;
});
chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.status === 'complete' && tab.url?.startsWith(`${ORIGIN}/`)) void pageLoaded(tabId);
});
chrome.runtime.onInstalled.addListener(() => void round());
chrome.runtime.onStartup.addListener(() => void round());
chrome.alarms.onAlarm.addListener((alarm) => {
  switch (alarm.name) {
    case SWEEP_ALARM:
      void sweep();
      break;
    case HELLO_ALARM:
      void hello();
      break;
    case LISTEN_ALARM:
      void listen();
      break;
    case UPDATE_ALARM:
      // The browser looks for a new version of an extension that a policy installs only every few hours: the extension asks for it itself, every hour.
      chrome.runtime.requestUpdateCheck().catch(() => {});
      break;
    default:
  }
});

// Created only when missing or changed: creating one again restarts its period, and the worker wakes up often.
async function ensureAlarm(name, periodInMinutes) {
  const existing = await chrome.alarms.get(name);
  if (existing?.periodInMinutes !== periodInMinutes) chrome.alarms.create(name, { periodInMinutes });
}
// A service worker is not a module: it cannot await at the top level (S7785), so these calls start the alarms and are not awaited.
void ensureAlarm(SWEEP_ALARM, 1); // NOSONAR
void ensureAlarm(UPDATE_ALARM, 60); // NOSONAR
void ensureAlarm(HELLO_ALARM, HELLO_MINUTES); // NOSONAR
void ensureAlarm(LISTEN_ALARM, HELLO_MINUTES); // NOSONAR
