import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, type PlaywrightWorkerArgs, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';

/** The operator of the e2e stack (tests/e2e/stack/docker-compose.yml): a test-only value. */
const OPERATOR = { name: 'e2e-operator', password: 'e2e-admin-password' };

/** A name that nobody else uses: the lockout counts per name, and the operator's must stay free for the other tests. */
/** A value that a test needs and that the configuration must give: its absence is a failure of the setup, said as one. */
function required<T>(value: T | undefined | null): T {
  if (value === undefined || value === null) {
    throw new Error('A value that the test needs is missing.');
  }
  return value;
}

const unique = (prefix: string) => `${prefix}-${randomUUID().slice(0, 8)}`;

/** A request context that has signed in: it keeps the session cookie, and sends the header a write needs. */
async function signedInRequest(playwright: PlaywrightWorkerArgs['playwright'], baseURL: string) {
  const request = await playwright.request.newContext({ baseURL, extraHTTPHeaders: { 'X-Geminarr-Request': '1' } });
  const login = await request.post('/api/v1/auth/login', { data: OPERATOR });
  expect(login.status()).toBe(200);
  return request;
}

/** Opens the page and signs in through the form, like a person. */
async function signIn(page: Page, path = '/') {
  await page.goto(path);
  await page.getByLabel('Name').fill(OPERATOR.name);
  await page.getByLabel('Password').fill(OPERATOR.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('navigation', { name: 'Main menu' })).toBeVisible();
}

test.describe('smoke @smoke', () => {
  test('the application is alive and ready, with no sign-in', { tag: '@browserless' }, async ({ request }) => {
    expect((await request.get('/health/live')).status()).toBe(200);
    expect((await request.get('/health/ready')).status()).toBe(200);
  });

  test(
    'nothing but the probes and the page is open to a caller who has not signed in',
    { tag: '@browserless' },
    async ({ request }) => {
      for (const path of [
        '/api/v1/system/status',
        '/api/v1/health',
        '/api/v1/settings',
        '/api/v1/auth/session',
        '/openapi/v1.json',
        '/api/v1/nothing-here',
      ]) {
        expect((await request.get(path)).status(), path).toBe(401);
      }
    },
  );

  test(
    'on the real image, once signed in, an unknown API path or file is a 404 and not the HTML shell',
    { tag: '@browserless' },
    async ({ playwright, baseURL }) => {
      const request = await signedInRequest(playwright, baseURL ?? '');
      for (const path of ['/api/v1/nothing-here', '/missing.js']) {
        const response = await request.get(path);

        expect(response.status(), path).toBe(404);
        expect(response.headers()['content-type'] ?? '', path).not.toContain('text/html');
      }
      await request.dispose();
    },
  );

  test(
    'a write without the header is refused, even the sign-in itself',
    { tag: '@browserless' },
    async ({ request }) => {
      const response = await request.post('/api/v1/auth/login', { data: OPERATOR });

      expect(response.status()).toBe(403);
      expect(response.headers()['set-cookie']).toBeUndefined();
    },
  );

  test(
    'a name that is not one of the page is refused, and the probes are not',
    { tag: '@browserless' },
    async ({ request }) => {
      expect((await request.get('/api/v1/system/status', { headers: { Host: 'evil.example' } })).status()).toBe(400);
      expect((await request.get('/health/ready', { headers: { Host: '10.42.0.17:8282' } })).status()).toBe(200);
    },
  );

  test(
    'the real server refuses a sign-in body that is larger than a name and a password can be',
    { tag: '@browserless' },
    async ({ request }) => {
      const response = await request.post('/api/v1/auth/login', {
        headers: { 'X-Geminarr-Request': '1' },
        data: { name: 'operator', password: 'p'.repeat(20_000) },
      });

      expect(response.status()).toBe(413);
    },
  );

  test('five wrong passwords lock a name, with the time to wait', { tag: '@browserless' }, async ({ request }) => {
    const name = unique('guess');
    for (let attempt = 0; attempt < 5; attempt++) {
      const wrong = await request.post('/api/v1/auth/login', {
        headers: { 'X-Geminarr-Request': '1' },
        data: { name, password: 'wrong password' },
      });
      expect(wrong.status()).toBe(401);
    }

    const locked = await request.post('/api/v1/auth/login', {
      headers: { 'X-Geminarr-Request': '1' },
      data: { name, password: 'wrong password' },
    });

    expect(locked.status()).toBe(429);
    expect(Number(locked.headers()['retry-after'])).toBeGreaterThan(0);
  });

  test('the sign-in page has no accessibility violation and says nothing of the application', async ({ page }) => {
    await page.goto('/');

    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
    await expect(page.getByRole('navigation', { name: 'Main menu' })).toHaveCount(0);
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
  });

  test('a wrong password is told the same way as a wrong name', async ({ page }) => {
    await page.goto('/');
    await page.getByLabel('Name').fill(unique('someone'));
    await page.getByLabel('Password').fill('a wrong password');
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page.getByRole('alert')).toHaveText('The name or the password is wrong.');
  });

  test('the operator signs in, sees the health, and the cookie is one a script cannot read', async ({
    page,
    context,
  }) => {
    await signIn(page);

    await expect(page.getByText('API reachable')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Health' })).toBeVisible();
    await expect(page.getByText('Data folder')).toBeVisible();
    const cookie = (await context.cookies()).find((candidate) => candidate.name === 'geminarr.session');
    expect(cookie).toMatchObject({ httpOnly: true, sameSite: 'Strict' });
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
  });

  test('the settings page shows the settings with the secrets hidden', async ({ page }) => {
    await signIn(page, '/settings');

    await page.getByRole('link', { name: 'Settings' }).click();
    await expect(page.getByText('GEMINARR__DATAFOLDER')).toBeVisible();
    await expect(page.getByText('set (secret)').first()).toBeVisible();
    await expect(page.getByText('e2e-admin-password')).toHaveCount(0);
    // The name of the operator is in the header as who is signed in; the list of settings does not repeat it.
    await expect(page.getByRole('region', { name: 'Settings' }).getByText('e2e-operator')).toHaveCount(0);
  });

  test('the menu ends with who this environment is: its name, its picture and the version', async ({
    page,
    request,
  }) => {
    await signIn(page);

    const menu = page.getByRole('complementary');
    await expect(menu.getByText('E2E Lab')).toBeVisible();
    await expect(menu.getByText(/^v\d/)).toBeVisible();
    const logo = menu.getByRole('img', { name: 'E2E Lab' });
    await expect(logo).toBeVisible();
    expect(await logo.evaluate((image: HTMLImageElement) => image.naturalWidth > 0)).toBe(true);
    // The picture is for the person who is signed in only.
    const src = (await logo.getAttribute('src')) ?? '';
    const cookies = await page.context().cookies();
    const picture = await request.get(src, {
      headers: { Cookie: cookies.map((c) => `${c.name}=${c.value}`).join('; ') },
    });
    expect(picture.status()).toBe(200);
    expect(picture.headers()['content-type']).toBe('image/svg+xml');
    expect((await request.get(src)).status()).toBe(401);
  });

  test('the overview says it is not connected and not linked, shows the passes, and runs one on request', async ({
    page,
  }) => {
    await signIn(page);

    const connection = page.getByRole('region', { name: 'Connection' });
    await expect(connection.getByText('Not connected', { exact: true })).toBeVisible();
    await expect(connection.getByText('Not linked')).toBeVisible();
    // A pass runs a few seconds after the start, and again when asked: each is a row, in counts only.
    const passes = page.getByRole('region', { name: 'Latest passes' });
    await expect(passes.getByText(/turns sent/).first()).toBeVisible({ timeout: 20_000 });
    // The card holds a few rows only, so a count could not grow: the id of the latest run does.
    const latestRunId = async (): Promise<number> =>
      ((await (await page.request.get('/api/v1/runs?limit=1')).json()) as { runs: { id: number }[] }).runs[0]?.id ?? 0;
    const before = await latestRunId();
    await connection.getByRole('button', { name: 'Run now' }).click();
    await expect(async () => expect(await latestRunId()).toBeGreaterThan(before)).toPass({ timeout: 20_000 });
    // The health names what is not there yet, with a sentence and no code.
    const health = page.getByRole('region', { name: 'Health' });
    await expect(health.getByText(/No Mnemarr is linked: nothing is sent/)).toBeVisible();
    await expect(health.getByText('Database')).toBeVisible();
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
  });

  test('the Mnemarr page asks for an address, keeps it (the browsers share one server), and offers to pair', async ({
    page,
  }) => {
    await signIn(page);
    try {
      await page.getByRole('link', { name: 'Mnemarr' }).first().click();

      await expect(page.getByRole('heading', { name: 'Mnemarr', level: 1 })).toBeVisible();
      await page.getByLabel('Address of Mnemarr').fill('https://mnemarr.homelab.example');
      await page.getByRole('button', { name: 'Save the address' }).click();
      await expect(page.getByText('https://mnemarr.homelab.example', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Pair with Mnemarr' })).toBeEnabled();
      expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
    } finally {
      // The browsers share one server: the address does not stay for the tests that read "Not linked".
      expect((await page.request.delete('/api/v1/mnemarr', { headers: { 'X-Geminarr-Request': '1' } })).status()).toBe(
        204,
      );
    }
  });

  test('the Mnemarr page is closed without a sign-in', { tag: '@browserless' }, async ({ request }) => {
    for (const path of ['/api/v1/mnemarr', '/api/v1/mnemarr/pairing', '/api/v1/runs', '/api/v1/connection']) {
      expect((await request.get(path)).status(), path).toBe(401);
    }
    expect((await request.post('/api/v1/mnemarr/pairing')).status()).toBe(403);
  });

  test('the operator signs in through the live view, the session holds, expires and is destroyed', async ({
    page,
    request,
    playwright,
    baseURL,
    browserName,
  }) => {
    // The browser of the container is one and the two projects share one server: the live view is walked once.
    test.skip(browserName !== 'chromium', 'one live view at a time, on one server');
    test.setTimeout(240_000);
    // Another test may have given an address of Mnemarr: a pass would wait for a Mnemarr that is not there, and this test is about the session.
    const api = await signedInRequest(playwright, required(baseURL));
    expect((await api.delete('/api/v1/mnemarr')).status()).toBe(204);
    await signIn(page);
    await page.getByRole('link', { name: 'Connection' }).first().click();
    await expect(page.getByText('Not connected', { exact: true })).toBeVisible();

    // The live view: the screen of the container's browser on a canvas.
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await expect(page.getByText(/Time left/)).toBeVisible({ timeout: 60_000 });
    await expect(page.getByText('You are controlling the browser of the Geminarr container.')).toBeVisible();
    const canvas = page.locator('canvas');
    // The fake sign-in page is laid out in absolute positions: click the fields on the canvas and type with the keyboard, as a person would.
    // Wait for the sign-in page itself to be on the screen: the button of the fake page is grey on a white page, at a known place.
    await expect(async () => {
      const onScreen = await canvas.evaluate((c: HTMLCanvasElement) => {
        const context = c.getContext('2d');
        const red = context ? (context.getImageData(150, 235, 1, 1).data[0] ?? 255) : 255;
        return red < 250;
      });
      expect(onScreen).toBe(true);
    }).toPass({ timeout: 30_000 });
    await expect(page.getByText('You are controlling the browser of the Geminarr container.')).toBeInViewport();
    // The page may scroll while it is used: the place of the canvas is read again before each click, and it is kept in view.
    const click = async (x: number, y: number) => {
      await canvas.scrollIntoViewIfNeeded();
      const box = await canvas.boundingBox();
      if (!box) {
        throw new Error('The screen is not on the page.');
      }
      const scale = box.width / 1280;
      await page.mouse.click(box.x + x * scale, box.y + y * scale);
    };
    await click(200, 120);
    await page.keyboard.type('dedicated-account');
    await click(200, 180);
    await page.keyboard.type('a password with spaces');
    await click(200, 240);

    // The live view sees the assistant's page: the session is made and the live view closes by itself.
    await expect(page.getByText('Connected', { exact: true })).toBeVisible({ timeout: 60_000 });
    await expect(page.getByText(/Time left/)).toHaveCount(0);
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);

    // A pass checks the session in a browser without a window, with the profile: it is good, and the state stays connected.
    await page.getByRole('link', { name: 'Overview' }).click();
    const passes = page.getByRole('region', { name: 'Latest passes' });
    const connection = page.getByRole('region', { name: 'Connection' });
    await expect(passes.getByText(/turns sent/).first()).toBeVisible({ timeout: 30_000 });
    const before = await passes.getByText(/turns sent/).count();
    await connection.getByRole('button', { name: 'Run now' }).click();
    await expect(async () => expect(await passes.getByText(/turns sent/).count()).toBeGreaterThan(before)).toPass({
      timeout: 60_000,
    });
    await expect(connection.getByText('Connected', { exact: true })).toBeVisible();

    // The assistant ends the session (an expiry): the next pass finds nobody signed in, and the state asks to sign in again.
    expect((await request.post('http://localhost:8091/admin/expire')).status()).toBe(204);
    await connection.getByRole('button', { name: 'Run now' }).click();
    await expect(connection.getByText('Sign in again', { exact: true })).toBeVisible({ timeout: 90_000 });

    // Disconnect destroys the profile: the state is not connected, and a pass has nothing to check.
    await page.getByRole('link', { name: 'Connection' }).first().click();
    await page.getByRole('button', { name: 'Disconnect', exact: true }).click();
    await page.getByRole('button', { name: 'Yes, disconnect' }).click();
    await expect(page.getByText('Not connected', { exact: true })).toBeVisible({ timeout: 30_000 });
  });

  test(
    'the live view refuses a viewer that has no token, and another origin',
    { tag: '@browserless' },
    async ({ playwright, baseURL }) => {
      const api = await playwright.request.newContext({
        baseURL: required(baseURL),
        extraHTTPHeaders: { 'X-Geminarr-Request': '1' },
      });
      expect((await api.post('/api/v1/auth/login', { data: OPERATOR })).status()).toBe(200);
      const ticket = (await (await api.post('/api/v1/connection/live-views')).json()) as {
        id: string;
        token: string;
        path: string;
      };
      // A WebSocket upgrade without the right token, or from another origin, is refused before the browser starts.
      const cookie = (await api.storageState()).cookies.map((c) => `${c.name}=${c.value}`).join('; ');
      const open = async (origin: string, token: string) => {
        const socket = new WebSocket(`ws://${new URL(required(baseURL)).host}${ticket.path}?token=${token}`, {
          headers: { Cookie: cookie, Origin: origin },
        } as never);
        return await new Promise<string>((resolve) => {
          socket.onopen = () => resolve('open');
          socket.onerror = () => resolve('refused');
        });
      };
      expect(await open('http://evil.example', ticket.token)).toBe('refused');
      expect(await open(required(baseURL), 'not-the-token')).toBe('refused');
      expect((await api.delete('/api/v1/connection/live-views')).status()).toBe(204);
    },
  );

  test('the system status page shows the version of the running build', async ({ page }) => {
    await signIn(page, '/system/status');

    await page.getByRole('link', { name: 'System' }).click();
    await expect(page.getByRole('heading', { name: 'Status' })).toBeVisible();
    await expect(page.getByText('Version', { exact: true })).toBeVisible();
    await expect(page.getByText('Runtime', { exact: true })).toBeVisible();
  });

  test('signing out ends the session on the server: the cookie that was kept is dead', async ({
    page,
    context,
    playwright,
    baseURL,
  }) => {
    await signIn(page);
    const cookie = (await context.cookies()).find((candidate) => candidate.name === 'geminarr.session');
    const copy = await playwright.request.newContext({
      baseURL,
      extraHTTPHeaders: { Cookie: `geminarr.session=${cookie?.value ?? ''}` },
    });
    expect((await copy.get('/api/v1/auth/session')).status()).toBe(200);

    await page.getByRole('button', { name: 'Sign out' }).click();

    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
    expect((await copy.get('/api/v1/auth/session')).status()).toBe(401);
    await copy.dispose();
  });

  test('the page is served with its security headers', { tag: '@browserless' }, async ({ request }) => {
    const response = await request.get('/');

    expect(response.headers()['x-content-type-options']).toBe('nosniff');
    expect(response.headers()['x-frame-options']).toBe('DENY');
    expect(response.headers()['referrer-policy']).toBe('no-referrer');
    expect(response.headers()['content-security-policy']).toContain("frame-ancestors 'none'");
  });

  test(
    'the page is looked at again each time and the scripts it names are kept for good',
    { tag: '@browserless' },
    async ({ request }) => {
      const page = await request.get('/');
      expect(page.headers()['cache-control']).toContain('no-cache');

      const script = /src="(\/assets\/[^"]+\.js)"/.exec(await page.text())?.[1];
      expect(script, 'the page names a script under /assets').toBeTruthy();
      expect((await request.get(script ?? '')).headers()['cache-control']).toContain('immutable');
    },
  );

  test(
    'an unknown route of the application is the page, and the API description is closed and not cached',
    { tag: '@browserless' },
    async ({ request, playwright, baseURL }) => {
      const route = await request.get('/somewhere/in/the/application');
      expect(route.status()).toBe(200);
      expect(route.headers()['content-type']).toContain('text/html');

      const signedIn = await signedInRequest(playwright, baseURL ?? '');
      expect((await signedIn.get('/openapi/v1.json')).headers()['cache-control']).toContain('no-store');
      await signedIn.dispose();
    },
  );
});
