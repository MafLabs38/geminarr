// The only place that talks to Geminarr, and the one that opens the pages of the assistant. Its address and its token come from the policy of the browser (managed storage), never from the code,
// and it only ever sends to that address. A page of the assistant is opened in the background, read by content.js, and closed.
const ORIGIN = '__ASSISTANT_ORIGIN__';
const ROUND_ALARM = 'round';
const DEFAULT_ROUND_MINUTES = 15;
const MAX_ROUND_MINUTES = 1440;
const SWEEP_ALARM = 'sweep';
const UPDATE_ALARM = 'update';
const WORK_TABS = 'workTabs';
// A page that has not answered after this long is closed: a hung tab must not pile up.
const TAB_LIFETIME_MS = 3 * 60 * 1000;

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

async function remember(tabId) {
  const { [WORK_TABS]: tabs = {} } = await chrome.storage.session.get(WORK_TABS);
  tabs[tabId] = Date.now();
  await chrome.storage.session.set({ [WORK_TABS]: tabs });
}

async function forget(tabId) {
  const { [WORK_TABS]: tabs = {} } = await chrome.storage.session.get(WORK_TABS);
  await chrome.storage.session.set({
    [WORK_TABS]: Object.fromEntries(Object.entries(tabs).filter(([id]) => id !== String(tabId))),
  });
}

async function openInBackground(path) {
  const tab = await chrome.tabs.create({ url: `${ORIGIN}${path}`, active: false });
  await remember(tab.id);
}

async function isWorkTab(tabId) {
  const { [WORK_TABS]: tabs = {} } = await chrome.storage.session.get(WORK_TABS);
  return String(tabId) in tabs;
}

async function closeTab(tabId) {
  await forget(tabId);
  await chrome.tabs.remove(tabId).catch(() => {});
}

/** A round: one page of the list. What it finds, and what Geminarr asks for, is done by the messages below. */
async function round() {
  if (await settings()) await openInBackground('/app');
}

async function sweep() {
  const { [WORK_TABS]: tabs = {} } = await chrome.storage.session.get(WORK_TABS);
  for (const [tabId, since] of Object.entries(tabs)) {
    if (Date.now() - since > TAB_LIFETIME_MS) await closeTab(Number(tabId));
  }
}

async function onMessage(message, sender) {
  const answer = await push(message);
  if (message.kind === 'list') {
    for (const id of (answer?.open ?? []).filter((candidate) => /^[A-Za-z0-9_-]{1,100}$/.test(candidate)))
      await openInBackground(`/app/${id}`);
  }
  // A page that this extension opened is done once it has been read: the list when Geminarr has said what it wants, a conversation once handed over. A page that a person opened is theirs, and stays.
  if (sender.tab && (message.kind === 'list' || message.kind === 'conversation') && (await isWorkTab(sender.tab.id)))
    await closeTab(sender.tab.id);
}

chrome.runtime.onMessage.addListener((message, sender) => {
  void onMessage(message, sender);
});
chrome.runtime.onInstalled.addListener(() => void round());
chrome.runtime.onStartup.addListener(() => void round());
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
chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === ROUND_ALARM) void round();
  if (alarm.name === SWEEP_ALARM) void sweep();
});
// The browser looks for a new version of an extension that a policy installs only every few hours: the extension asks for it itself, every hour.
chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === UPDATE_ALARM) void chrome.runtime.requestUpdateCheck().catch(() => {});
});
