import { expect, type PlaywrightWorkerArgs, test } from '@playwright/test';

/** The operator of the e2e stack (tests/e2e/stack/docker-compose.yml): a test-only value. */
const OPERATOR = { name: 'e2e-operator', password: 'e2e-admin-password' };
/** The instance of the stack that reads through the extension, in the browser container (the other one is a fresh deployment). */
const GEMINARR = process.env.GEMINARR_URL ?? 'http://localhost:8282';
/** What the stack publishes for the tests: the fake assistant (to sign it out and to add a conversation) and the fake Mnemarr (to read what it holds). */
const ASSISTANT = 'http://localhost:8091';
const MNEMARR = 'http://localhost:8092';

interface Connection {
  state: string;
  reason: string | null;
  reader: string;
  account: { name: string | null; email: string } | null;
  signingIn: boolean;
}

interface ConversationsPage {
  total: number;
  backfill: { enabled: boolean; perPass: number; listed: number; known: number; listComplete: boolean; done: boolean };
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
const connectionOf = async (request: Awaited<ReturnType<typeof signedInRequest>>) =>
  (await (await request.get('/api/v1/connection')).json()) as Connection;

/** What the fake assistant counts: the pages that it showed to a visitor (a request without the cookie of a session). */
async function guests(playwright: PlaywrightWorkerArgs['playwright']): Promise<number> {
  const request = await playwright.request.newContext();
  const answer = (await (await request.get(`${ASSISTANT}/admin/guests`)).json()) as { guests: number };
  await request.dispose();
  return answer.guests;
}

// The real extension, in a real Chromium that a policy installed it in, reading a fake assistant and handing what it reads to Geminarr, which sends it to a fake Mnemarr. Nothing is simulated in between. The person
// who signs in by hand is played by the fake assistant, which gives a cookie to the next visitor when a test says that somebody signs in.
// One fake assistant and one extension are shared by everything below: it all runs in order, on one worker (a parallel run would sign the one account out under the other group).
test.describe('the journey', () => {
  test.describe.configure({ mode: 'serial' });

  test.describe('a person signs in, the extension reads the assistant, and the operator disconnects @smoke', () => {
    test(
      'the page notices the sign-in, says who signed in, and the conversations reach Mnemarr, turn by turn',
      { tag: '@browserless' },
      async ({ playwright }) => {
        test.setTimeout(900_000);
        const request = await signedInRequest(playwright, GEMINARR);
        const assistant = await playwright.request.newContext();
        // Whatever a previous attempt left (the group is run again when a test of it fails), nobody is signed in to the assistant at the start.
        await assistant.post(`${ASSISTANT}/admin/signout`);

        // Nobody has signed in to the browser: the first rounds of the extension find a visitor, and the page asks to sign in.
        await expect
          .poll(async () => (await connectionOf(request)).state, {
            timeout: 360_000,
            intervals: [3000],
            message: 'the page asks to sign in',
          })
          .toBe('NeedsLogin');
        expect((await connectionOf(request)).account).toBeNull();

        // The operator presses Sign in (the desktop is asked for), and a person signs in: the extension is asked to look often, and the page follows.
        expect((await request.post('/api/v1/connection/desktop')).status()).toBe(200);
        expect((await connectionOf(request)).signingIn).toBe(true);
        expect((await assistant.post(`${ASSISTANT}/admin/signin`)).status()).toBe(204);
        await expect
          .poll(async () => (await connectionOf(request)).state, {
            timeout: 120_000,
            intervals: [3000],
            message: 'the sign-in is noticed within a minute, without waiting for the hour of a pass',
          })
          .toBe('Connected');
        const connected = await connectionOf(request);
        expect(connected.account).toEqual({ name: 'Jane Doe', email: 'jane.doe@example.org' });
        expect(connected.signingIn).toBe(false);

        await expect
          .poll(async () => (await heldByMnemarr(playwright))['gemini-c1a2b3c4d5e6'] ?? 0, {
            timeout: 360_000,
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
        const runs = ((await (await request.get('/api/v1/runs')).json()) as { runs: Run[] }).runs;
        expect(runs.reduce((total, run) => total + run.turnsSent, 0)).toBe(8);
        await assistant.dispose();
        await request.dispose();
      },
    );

    test(
      'a conversation added later is read in the next round and none is sent twice',
      { tag: '@browserless' },
      async ({ playwright }) => {
        test.setTimeout(900_000);
        const before = await heldByMnemarr(playwright);

        const assistant = await playwright.request.newContext();
        await assistant.post(`${ASSISTANT}/admin/add`);
        await assistant.dispose();
        // The stack asks the extension for a round every minute (the policy of its browser says so): the new conversation comes with the next one.
        await expect
          .poll(async () => Object.keys(await heldByMnemarr(playwright)).length, {
            timeout: 360_000,
            intervals: [5000],
            message: 'the added conversation reaches Mnemarr',
          })
          .toBe(Object.keys(before).length + 1);
        const after = await heldByMnemarr(playwright);
        for (const [session, lines] of Object.entries(before)) expect(after[session]).toBe(lines);
      },
    );

    test(
      'the history below the latest conversations is walked a few at a time, and the page says how far it is',
      { tag: '@browserless' },
      async ({ playwright }) => {
        test.setTimeout(1_300_000);
        const request = await signedInRequest(playwright, GEMINARR);
        const assistant = await playwright.request.newContext();
        const before = Object.keys(await heldByMnemarr(playwright)).length;
        const read = async () => (await (await request.get('/api/v1/conversations')).json()) as ConversationsPage;

        await assistant.post(`${ASSISTANT}/admin/history`);
        // Four older ones are read in a round, whatever the list holds: after the first round some are in, and not yet all.
        let firstRound = 0;
        await expect
          .poll(
            async () => {
              firstRound = Object.keys(await heldByMnemarr(playwright)).length - before;
              return firstRound;
            },
            { timeout: 360_000, intervals: [3000], message: 'some of the older conversations reach Mnemarr' },
          )
          .toBeGreaterThan(0);
        // A few at a time: not all nine in the first round.
        expect(firstRound).toBeLessThan(9);
        await expect
          .poll(async () => Object.keys(await heldByMnemarr(playwright)).length - before, {
            timeout: 600_000,
            intervals: [5000],
            message: 'all nine older conversations reach Mnemarr',
          })
          .toBe(9);
        await expect
          .poll(async () => (await read()).backfill.done, {
            timeout: 120_000,
            intervals: [3000],
            message: 'the page says that the history is read',
          })
          .toBe(true);
        const page = await read();
        expect(page.backfill).toMatchObject({ enabled: true, perPass: 4, listComplete: true });
        expect(page.backfill.known).toBe(page.backfill.listed);
        // Metadata only: no title and no turn, whatever was read.
        expect(JSON.stringify(page)).not.toMatch(/older conversation|old question|old answer/iu);
        await assistant.dispose();
        await request.dispose();
      },
    );

    test(
      'a page that nobody is signed in to says "sign in again", and reading resumes when somebody is',
      { tag: '@browserless' },
      async ({ playwright }) => {
        test.setTimeout(900_000);
        const request = await signedInRequest(playwright, GEMINARR);
        const assistant = await playwright.request.newContext();
        const state = async () => ((await (await request.get('/api/v1/connection')).json()) as Connection).state;

        try {
          await assistant.post(`${ASSISTANT}/admin/signout`);
          await expect
            .poll(state, { timeout: 360_000, intervals: [5000], message: 'the page asks to sign in again' })
            .toBe('NeedsLogin');
        } finally {
          // Whatever happened, the fake assistant is signed in again for what comes next.
          await assistant.post(`${ASSISTANT}/admin/signin`);
        }
        await expect.poll(state, { timeout: 360_000, intervals: [5000], message: 'reading resumes' }).toBe('Connected');
        await assistant.dispose();
        await request.dispose();
      },
    );

    test(
      'a page that is not the one the extension knows says "reader broken", not "sign in again", and reading resumes when it is',
      { tag: '@browserless' },
      async ({ playwright }) => {
        test.setTimeout(900_000);
        const request = await signedInRequest(playwright, GEMINARR);
        const assistant = await playwright.request.newContext();
        try {
          const connection = async () =>
            ((await (await request.get('/api/v1/connection')).json()) as Connection).reason;

          await assistant.post(`${ASSISTANT}/admin/break`);
          await expect
            .poll(connection, { timeout: 360_000, intervals: [5000], message: 'the reader is said to be broken' })
            .toBe('reader-broken');
          await assistant.post(`${ASSISTANT}/admin/signin`);
          await expect.poll(connection, { timeout: 360_000, intervals: [5000], message: 'reading resumes' }).toBeNull();
        } finally {
          await assistant.dispose();
          await request.dispose();
        }
      },
    );

    test(
      'the operator disconnects: the extension signs the account out of the browser, the connection is forgotten without asking anybody to sign in again, and a person can sign in again',
      { tag: '@browserless' },
      async ({ playwright }) => {
        test.setTimeout(900_000);
        const request = await signedInRequest(playwright, GEMINARR);
        const assistant = await playwright.request.newContext();
        const seenBefore = await guests(playwright);
        await expect
          .poll(async () => (await connectionOf(request)).state, {
            timeout: 120_000,
            intervals: [3000],
            message: 'the page is connected',
          })
          .toBe('Connected');

        expect((await request.post('/api/v1/connection/disconnect')).status()).toBe(200);
        expect(await connectionOf(request)).toMatchObject({ state: 'NotConnected', account: null });

        // The extension is asked the next time that it says that it is there: it deletes the cookies of its browser and looks at the page, which now shows a visitor.
        await expect
          .poll(() => guests(playwright), {
            timeout: 120_000,
            intervals: [3000],
            message: 'the cookies were deleted and the page looked at',
          })
          .toBeGreaterThan(seenBefore);
        // A session that is gone because the operator asked is not a login that expired: the connection stays "not connected", and nothing asks for a sign-in.
        await new Promise((resolve) => setTimeout(resolve, 8000));
        expect(await connectionOf(request)).toMatchObject({
          state: 'NotConnected',
          reason: 'disconnected',
          account: null,
        });

        expect((await request.post('/api/v1/connection/desktop')).status()).toBe(200);
        expect((await assistant.post(`${ASSISTANT}/admin/signin`)).status()).toBe(204);
        await expect
          .poll(async () => (await connectionOf(request)).account?.email, {
            timeout: 120_000,
            intervals: [3000],
            message: 'somebody signs in again and the page says who',
          })
          .toBe('jane.doe@example.org');
        await assistant.dispose();
        await request.dispose();
      },
    );
  });

  interface AccountRow {
    id: string;
    machine: string | null;
    active: boolean;
    signedIn: boolean;
    linked: boolean;
    conversations: number;
  }

  type Operator = Awaited<ReturnType<typeof signedInRequest>>;

  const accountsOf = async (request: Operator) =>
    ((await (await request.get('/api/v1/accounts')).json()) as { accounts: AccountRow[] }).accounts;

  async function heldBy(
    playwright: PlaywrightWorkerArgs['playwright'],
    machine: string,
  ): Promise<Record<string, number>> {
    const request = await playwright.request.newContext();
    const held = (await (await request.get(`${MNEMARR}/admin/conversations?machine=${machine}`)).json()) as Record<
      string,
      number
    >;
    await request.dispose();
    return held;
  }

  /** The operator disconnects (the extension deletes the cookies of the browser, so nobody is signed in), then a person signs in to the assistant as the address, and the page says whose account it is. */
  async function switchTo(playwright: PlaywrightWorkerArgs['playwright'], request: Operator, address: string) {
    const assistant = await playwright.request.newContext();
    const seen = await guests(playwright);
    expect((await request.post('/api/v1/connection/disconnect')).status()).toBe(200);
    await expect
      .poll(() => guests(playwright), { timeout: 120_000, intervals: [3000], message: 'the cookies were deleted' })
      .toBeGreaterThan(seen);
    expect((await request.post('/api/v1/connection/desktop')).status()).toBe(200);
    expect((await assistant.post(`${ASSISTANT}/admin/signin?as=${address}`)).status()).toBe(204);
    await expect
      .poll(async () => (await connectionOf(request)).account?.email, {
        timeout: 120_000,
        intervals: [3000],
        message: `the page says that ${address} signed in`,
      })
      .toBe(address);
    await assistant.dispose();
  }

  /** Pairs the machine of the account that is signed in (the fake Mnemarr approves at once) and gives its name. */
  async function pairActive(request: Operator): Promise<string> {
    expect((await request.post('/api/v1/mnemarr/pairing')).status()).toBe(200);
    const checked = (await (await request.post('/api/v1/mnemarr/pairing/check')).json()) as { state: string };
    expect(checked.state).toBe('approved');
    const active = (await accountsOf(request)).find((account) => account.active);
    expect(active?.linked).toBe(true);
    return active?.machine ?? '';
  }

  // One Geminarr, several accounts: whichever account is signed in is read, into a machine of its own that is named from its address (SPEC §8). The first account is the one the group above left signed in, linked by its enrolment code.
  test.describe('two accounts, signed in in turn, are two machines of notes @smoke', () => {
    const SECOND = 'sam.lee@example.org';
    const THIRD = 'sam.lee@example.net';
    let firstId = '';
    let secondMachine = '';
    let thirdMachine = '';

    test(
      'a second account that signs in asks for its machine, named from its address, and its conversations reach that machine alone',
      { tag: '@browserless' },
      async ({ playwright }) => {
        test.setTimeout(900_000);
        const request = await signedInRequest(playwright, GEMINARR);
        const firstHeld = await heldBy(playwright, 'gemini');
        const first = (await accountsOf(request)).find((account) => account.active);
        expect(first).toMatchObject({ machine: 'gemini', linked: true });
        firstId = first?.id ?? '';

        await switchTo(playwright, request, SECOND);
        // Its workspace is new: nobody paired a machine for it, and the page says which one it will be.
        const before = (await accountsOf(request)).find((account) => account.active);
        expect(before).toMatchObject({ machine: 'geminarr-sam-lee', linked: false, signedIn: true });
        expect((await accountsOf(request)).find((account) => account.id === firstId)).toMatchObject({
          active: false,
          signedIn: false,
          linked: true,
        });

        secondMachine = await pairActive(request);
        expect(secondMachine).toBe('geminarr-sam-lee');
        await expect
          .poll(async () => Object.keys(await heldBy(playwright, secondMachine)).length, {
            timeout: 360_000,
            intervals: [3000],
            message: 'the conversations of the second account reach its machine',
          })
          .toBe(2);
        // The first account's machine is untouched.
        expect(await heldBy(playwright, 'gemini')).toEqual(firstHeld);
        await request.dispose();
      },
    );

    test(
      'the first account signs in again: reading resumes where Mnemarr says, nothing is sent twice, and the account that is read cannot be forgotten',
      { tag: '@browserless' },
      async ({ playwright }) => {
        test.setTimeout(900_000);
        const request = await signedInRequest(playwright, GEMINARR);
        const firstHeld = await heldBy(playwright, 'gemini');
        const secondHeld = await heldBy(playwright, secondMachine);

        await switchTo(playwright, request, 'jane.doe@example.org');
        // The list of passes is a window: the id of the latest one is what grows.
        const latestRun = async () =>
          (
            (await (await request.get('/api/v1/runs?limit=1')).json()) as {
              runs: { id: number; outcome: string | null }[];
            }
          ).runs[0];
        const runBefore = (await latestRun())?.id ?? 0;
        // A run appears when a pass starts: it is the pass that has ended, with what it sent, that proves that nothing was sent twice.
        await expect
          .poll(
            async () => {
              const run = await latestRun();
              return run?.outcome === null ? 0 : (run?.id ?? 0);
            },
            { timeout: 360_000, intervals: [3000], message: 'a pass of the first account ran and ended' },
          )
          .toBeGreaterThan(runBefore);
        expect(await heldBy(playwright, 'gemini')).toEqual(firstHeld);
        expect(await heldBy(playwright, secondMachine)).toEqual(secondHeld);
        expect((await request.delete(`/api/v1/accounts/${firstId}`)).status()).toBe(409);
        await request.dispose();
      },
    );

    test(
      'two addresses with the same first part get two machines with two names',
      { tag: '@browserless' },
      async ({ playwright }) => {
        test.setTimeout(900_000);
        const request = await signedInRequest(playwright, GEMINARR);

        await switchTo(playwright, request, THIRD);
        const third = await pairActive(request);
        thirdMachine = third;

        expect(third).toMatch(/^geminarr-sam-lee-[0-9a-f]{4}$/);
        expect(third).not.toBe(secondMachine);
        await expect
          .poll(async () => Object.keys(await heldBy(playwright, third)).length, {
            timeout: 360_000,
            intervals: [3000],
            message: 'the conversations of the third account reach its machine',
          })
          .toBe(2);
        await request.dispose();
      },
    );

    test(
      'an account that is forgotten is gone, and the others are untouched',
      { tag: '@browserless' },
      async ({ playwright }) => {
        test.setTimeout(900_000);
        const request = await signedInRequest(playwright, GEMINARR);

        await switchTo(playwright, request, 'jane.doe@example.org');
        const others = (await accountsOf(request)).filter((account) => account.id !== firstId);
        expect(others.map((account) => account.machine).sort()).toEqual([secondMachine, thirdMachine].sort());
        for (const account of others) {
          expect((await request.delete(`/api/v1/accounts/${account.id}`)).status()).toBe(204);
        }

        expect((await request.delete(`/api/v1/accounts/${others[0].id}`)).status()).toBe(404);
        expect(await accountsOf(request)).toEqual([
          expect.objectContaining({ id: firstId, linked: true, active: true }),
        ]);
        await request.dispose();
      },
    );
  });
});
