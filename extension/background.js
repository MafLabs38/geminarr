// The only place that talks to Geminarr, and the one that opens the pages of the assistant. Its address and its token come from the policy of the browser (managed storage), never from the code,
// and it only ever sends to that address. A page of the assistant is opened in the background, read by content.js, and closed.
const ORIGIN = '__ASSISTANT_ORIGIN__';
const ROUND_ALARM = 'round';
const DEFAULT_ROUND_MINUTES = 15;
const MAX_ROUND_MINUTES = 1440;
const SWEEP_ALARM = 'sweep';
const UPDATE_ALARM = 'update';
const WORK_TAB_PREFIX = 'workTab:';
// A page that has not answered after this long is closed: a hung tab must not pile up.
const TAB_LIFETIME_MS = 3 * 60 * 1000;
// At most this many conversations are opened for one answer, whatever the answer says: a tab is a page of the browser that holds the session.
const MAX_TABS_PER_ANSWER = 10;

async function settings() {
  const { geminarrUrl, token } = await chrome.storage.managed.get(['geminarrUrl', 'token']);
  if (!geminarrUrl || !token) return null;
  let base = String(geminarrUrl);
  while (base.endsWith('/')) base = base.slice(0, -1);
  return { base, token };
}

/** Sends what the page showed, and gives back what Geminarr answered (a list of conversations to read), or nothing when it did not take it. */
async function push(body) {
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

async function closeTab(tabId) {
  await chrome.storage.session.remove(workTabKey(tabId));
  await chrome.tabs.remove(tabId).catch(() => {});
}

async function hasWorkTab() {
  return Object.keys(await chrome.storage.session.get(null)).some((key) => key.startsWith(WORK_TAB_PREFIX));
}

/** A round: one page of the list. What it finds, and what Geminarr asks for, is done by the messages below. */
async function round() {
  // A round that finds the last one still reading (a slow page) waits: the sweep closes what hangs.
  if ((await settings()) && !(await hasWorkTab())) await openInBackground('/app');
}

async function sweep() {
  const everything = await chrome.storage.session.get(null);
  for (const [key, since] of Object.entries(everything)) {
    if (key.startsWith(WORK_TAB_PREFIX) && Date.now() - since > TAB_LIFETIME_MS)
      await closeTab(Number(key.slice(WORK_TAB_PREFIX.length)));
  }
}

async function onMessage(message, sender) {
  const answer = await push(message);
  if (message.kind === 'list') {
    const ids = (answer?.open ?? [])
      .filter((candidate) => /^[A-Za-z0-9_-]{1,64}$/.test(candidate))
      .slice(0, MAX_TABS_PER_ANSWER);
    for (const id of ids) await openInBackground(`/app/${id}`);
  }
  // A page that this extension opened is done once it has said what it had to say. A page that a person opened is theirs, and stays.
  if (sender.tab && (await isWorkTab(sender.tab.id))) await closeTab(sender.tab.id);
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
chrome.runtime.onInstalled.addListener(() => void round());
chrome.runtime.onStartup.addListener(() => void round());
chrome.alarms.onAlarm.addListener((alarm) => {
  switch (alarm.name) {
    case ROUND_ALARM:
      void round();
      break;
    case SWEEP_ALARM:
      void sweep();
      break;
    case UPDATE_ALARM:
      // The browser looks for a new version of an extension that a policy installs only every few hours: the extension asks for it itself, every hour.
      void chrome.runtime.requestUpdateCheck().catch(() => {});
      break;
    default:
  }
});

// Created only when missing or changed: creating one again restarts its period, and the worker wakes up often.
async function ensureAlarm(name, periodInMinutes) {
  const existing = await chrome.alarms.get(name);
  if (existing?.periodInMinutes !== periodInMinutes) chrome.alarms.create(name, { periodInMinutes });
}
async function roundMinutes() {
  const { roundMinutes: wanted } = await chrome.storage.managed.get('roundMinutes');
  return Number.isInteger(wanted) && wanted >= 1 && wanted <= MAX_ROUND_MINUTES ? wanted : DEFAULT_ROUND_MINUTES;
}
void roundMinutes().then((minutes) => ensureAlarm(ROUND_ALARM, minutes));
void ensureAlarm(SWEEP_ALARM, 1);
void ensureAlarm(UPDATE_ALARM, 60);
