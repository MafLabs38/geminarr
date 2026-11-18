import { expect, type PlaywrightWorkerArgs, test } from '@playwright/test';

/** The operator of the e2e stack (tests/e2e/stack/docker-compose.yml): a test-only value. */
const OPERATOR = { name: 'e2e-operator', password: 'e2e-admin-password' };
/** The instance of the stack that reads through the extension (the other one has no reader). */
const GEMINARR = process.env.GEMINARR_EXTENSION_URL ?? 'http://localhost:8283';
/** What the stack publishes for the tests: the fake assistant (to sign it out and to add a conversation) and the fake Mnemarr (to read what it holds). */
const ASSISTANT = 'http://localhost:8091';
const MNEMARR = 'http://localhost:8092';

interface Connection {
  state: string;
  reason: string | null;
  reader: string;
}

interface Run {
  outcome: string | null;
  listed: number;
  opened: number;
  turnsSent: number;
}

/** A request context that has signed in: it keeps the session cookie, and sends the header a write needs. */
async function signedInRequest(playwright: PlaywrightWorkerArgs['playwright'], baseURL: string) {
  const request = await playwright.request.newContext({ baseURL, extraHTTPHeaders: { 'X-Geminarr-Request': '1' } });
  const login = await request.post('/api/v1/auth/login', { data: OPERATOR });
  expect(login.status()).toBe(200);
  return request;
}

async function heldByMnemarr(playwright: PlaywrightWorkerArgs['playwright']): Promise<Record<string, number>> {
  const request = await playwright.request.newContext();
  const response = await request.get(`${MNEMARR}/admin/conversations`);
  const held = (await response.json()) as Record<string, number>;
  await request.dispose();
  return held;
}

// The real extension, in a real Chromium that a policy installed it in, reading a fake assistant and handing what it reads to Geminarr, which sends it to a fake Mnemarr. Nothing is simulated in between.
test.describe('the extension reads the assistant @smoke', () => {
  test.describe.configure({ mode: 'serial' });

  test(
    'it reads the conversations of the page and they reach Mnemarr, turn by turn',
    { tag: '@browserless' },
    async ({ playwright }) => {
      test.setTimeout(240_000);
      const request = await signedInRequest(playwright, GEMINARR);

      await expect
        .poll(async () => (await heldByMnemarr(playwright))['gemini-c1a2b3c4d5e6'] ?? 0, {
          timeout: 200_000,
          intervals: [3000],
          message: 'the first conversation reaches Mnemarr',
        })
        .toBeGreaterThan(0);
      await expect
        .poll(async () => Object.keys(await heldByMnemarr(playwright)).length, {
          timeout: 60_000,
          intervals: [3000],
          message: 'the conversations of the page reach Mnemarr',
        })
        .toBe(3);
      // The fake assistant has three conversations of 2, 4 and 2 turns: a meta line and the turns of each, once (on a fresh stack, as the pipeline's).
      expect(Object.values(await heldByMnemarr(playwright)).sort((a, b) => a - b)).toEqual([3, 3, 5]);

      const connection = (await (await request.get('/api/v1/connection')).json()) as Connection;
      expect(connection).toMatchObject({ state: 'Connected', reader: 'Web' });
      const runs = ((await (await request.get('/api/v1/runs')).json()) as { runs: Run[] }).runs;
      expect(runs.reduce((total, run) => total + run.turnsSent, 0)).toBe(8);
      await request.dispose();
    },
  );

  test(
    'a conversation added later is read in the next round and none is sent twice',
    { tag: '@browserless' },
    async ({ playwright }) => {
      test.setTimeout(240_000);
      const before = await heldByMnemarr(playwright);

      const assistant = await playwright.request.newContext();
      await assistant.post(`${ASSISTANT}/admin/add`);
      await assistant.dispose();
      // The stack asks the extension for a round every minute (the policy of its browser says so): the new conversation comes with the next one.
      await expect
        .poll(async () => Object.keys(await heldByMnemarr(playwright)).length, {
          timeout: 200_000,
          intervals: [5000],
          message: 'the added conversation reaches Mnemarr',
        })
        .toBe(Object.keys(before).length + 1);
      const after = await heldByMnemarr(playwright);
      for (const [session, lines] of Object.entries(before)) expect(after[session]).toBe(lines);
    },
  );

  test(
    'a page that nobody is signed in to says "sign in again", and reading resumes when somebody is',
    { tag: '@browserless' },
    async ({ playwright }) => {
      test.setTimeout(240_000);
      const request = await signedInRequest(playwright, GEMINARR);
      const assistant = await playwright.request.newContext();
      const state = async () => ((await (await request.get('/api/v1/connection')).json()) as Connection).state;

      try {
        await assistant.post(`${ASSISTANT}/admin/signout`);
        await expect
          .poll(state, { timeout: 200_000, intervals: [5000], message: 'the page asks to sign in again' })
          .toBe('NeedsLogin');
      } finally {
        // Whatever happened, the fake assistant is signed in again for what comes next.
        await assistant.post(`${ASSISTANT}/admin/signin`);
      }
      await expect.poll(state, { timeout: 200_000, intervals: [5000], message: 'reading resumes' }).toBe('Connected');
      await assistant.dispose();
      await request.dispose();
    },
  );

  test(
    'a page that is not the one the extension knows says "reader broken", not "sign in again", and reading resumes when it is',
    { tag: '@browserless' },
    async ({ playwright }) => {
      test.setTimeout(240_000);
      const request = await signedInRequest(playwright, GEMINARR);
      const assistant = await playwright.request.newContext();
      try {
        const connection = async () => ((await (await request.get('/api/v1/connection')).json()) as Connection).reason;

        await assistant.post(`${ASSISTANT}/admin/break`);
        await expect
          .poll(connection, { timeout: 200_000, intervals: [5000], message: 'the reader is said to be broken' })
          .toBe('reader-broken');
        await assistant.post(`${ASSISTANT}/admin/signin`);
        await expect.poll(connection, { timeout: 200_000, intervals: [5000], message: 'reading resumes' }).toBeNull();
      } finally {
        await assistant.dispose();
        await request.dispose();
      }
    },
  );
});
