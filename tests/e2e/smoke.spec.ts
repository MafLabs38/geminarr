import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, type PlaywrightWorkerArgs, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';

/** The operator of the e2e stack (tests/e2e/stack/docker-compose.yml): a test-only value. */
const OPERATOR = { name: 'e2e-operator', password: 'e2e-admin-password' };

/** A value that a test needs and that the configuration must give: its absence is a failure of the setup, said as one. */
function required<T>(value: T | undefined | null): T {
  if (value === undefined || value === null) {
    throw new Error('A value that the test needs is missing.');
  }
  return value;
}

/** A name that nobody else uses: the lockout counts per name, and the operator's must stay free for the other tests. */
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

  test('the overview says nobody has signed in to the browser and that nothing is linked, shows the passes, and runs one on request', async ({
    page,
  }) => {
    await signIn(page);

    const connection = page.getByRole('region', { name: 'Connection' });
    // Nobody has signed in to the browser: not connected before the first pass, degraded if that pass ran before the browser container was up, and asking for a sign-in once a pass has found nobody signed in.
    await expect(connection.getByText(/^(Not connected|Sign in again|Degraded)$/)).toBeVisible();
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

  test("the Connection page opens the browser's desktop, where a person signs in by hand", async ({
    page,
    playwright,
    baseURL,
    browserName,
  }) => {
    // One browser container and one server: the page is walked once, in one browser project.
    test.skip(browserName !== 'chromium', 'one browser container, one server');
    test.setTimeout(120_000);
    // Another test may have given an address of Mnemarr: a pass would wait for a Mnemarr that is not there, and this test is about the desktop.
    const api = await signedInRequest(playwright, required(baseURL));
    expect((await api.delete('/api/v1/mnemarr')).status()).toBe(204);
    await signIn(page);
    await page.getByRole('link', { name: 'Connection' }).first().click();

    // Nobody has signed in to the browser yet: the page says so, and its Sign in button opens the browser's desktop in a window of its own, on an origin of its own that a ticket of the operator's session opens
    const signInButton = page.getByRole('button', { name: 'Sign in to the assistant' });
    await expect(signInButton).toBeVisible({ timeout: 90_000 });
    const [desktop] = await Promise.all([page.context().waitForEvent('page'), signInButton.click()]);
    await expect(desktop).toHaveURL(/localhost:8283\/browser\/$/);
    await expect(desktop).toHaveTitle(/Chromium|Selkies/, { timeout: 60_000 });
    await desktop.close();
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
  });

  test(
    'the connection is closed without a sign-in, and the pause is refused without the header',
    { tag: '@browserless' },
    async ({ request }) => {
      expect((await request.get('/api/v1/connection')).status()).toBe(401);
      expect((await request.post('/api/v1/connection/pause', { data: { hours: 1 } })).status()).toBe(403);
    },
  );

  test("the web desktop of the browser is on an origin of its own, opened by a ticket of the operator's session @browserless", async ({
    request,
    playwright,
    baseURL,
  }) => {
    const desktopOrigin = 'http://localhost:8283';
    // Nothing of the desktop is reached without the cookie that a ticket gives, and a made-up ticket gives none.
    expect((await request.get(`${desktopOrigin}/browser/`, { maxRedirects: 0 })).status()).toBe(401);
    expect((await request.get(`${desktopOrigin}/browser/api/websockets`, { maxRedirects: 0 })).status()).toBe(401);
    expect((await request.get(`${desktopOrigin}/enter?ticket=not-a-ticket`, { maxRedirects: 0 })).status()).toBe(401);

    // The desktop is no port of the machine, and no other container of the network reaches it: it listens on the loopback of the namespace of the two containers, and it has no login of its own.
    await expect(request.get('http://localhost:3000/', { timeout: 5_000 })).rejects.toThrow();
    const reaches = async (port: number) =>
      (
        (await (await request.get(`http://localhost:8091/admin/reach?host=browser&port=${port}`)).json()) as {
          open: boolean;
        }
      ).open;
    expect(await reaches(8282), "the control: Geminarr's own port is open to the network").toBe(true);
    expect(await reaches(8283), "the control: so is the desktop's origin, which is closed without its cookie").toBe(
      true,
    );
    expect(await reaches(3000), 'the desktop itself is not').toBe(false);
    expect(await reaches(3001), 'nor its TLS port').toBe(false);
    expect(await reaches(9222), 'and there is no debugging port to look for').toBe(false);

    // The operator's session asks for the address, with a ticket; it is worth one opening.
    const api = await signedInRequest(playwright, required(baseURL));
    const asked = await api.post('/api/v1/connection/desktop');
    expect(asked.status()).toBe(200);
    const ticketUrl = new URL(((await asked.json()) as { url: string }).url);
    expect(ticketUrl.origin).toBe(desktopOrigin);
    const desktop = await playwright.request.newContext({ baseURL: desktopOrigin });
    const entered = await desktop.get(`${ticketUrl.pathname}${ticketUrl.search}`, { maxRedirects: 0 });
    expect(entered.status()).toBe(302);
    expect(entered.headers().location).toBe('/browser/');
    expect(
      (await request.get(`${desktopOrigin}${ticketUrl.pathname}${ticketUrl.search}`, { maxRedirects: 0 })).status(),
    ).toBe(401);

    const page = await desktop.get('/browser/');
    expect(page.status()).toBe(200);
    expect(await page.text()).toContain('Selkies');
    expect(page.headers()['set-cookie']).toBeUndefined();
    // The desktop's origin serves the desktop and nothing of Geminarr, and the interface's origin serves nothing of the desktop.
    expect((await desktop.get('/api/v1/connection')).status()).toBe(404);
    expect(await (await api.get('/browser/')).text()).not.toContain('Selkies');
    // What the desktop's web server offers beyond its client (its files, its other services) is not relayed, by any spelling of the path: the server merges slashes and decodes before it routes.
    for (const path of [
      '/browser/api/files',
      '/browser//api/files',
      '/browser/api%2Ffiles',
      '/browser/pelorus/',
      '/browser/api/',
    ]) {
      expect((await desktop.get(path)).status(), path).toBe(404);
    }
    expect((await desktop.post('/browser/api/clipboard')).status()).toBe(404);
    await desktop.dispose();
  });

  test("Geminarr's own process reaches nothing off the loopback, whatever the pod's policy allows for the browser (ADR 0020) @browserless", async ({
    request,
  }) => {
    const probe = async (port: number, host: string, target: number) =>
      (await (await request.get(`http://localhost:${port}/try?host=${host}&port=${target}`)).json()) as {
        open: boolean;
        uid: number;
      };
    const asGeminarr = await probe(9224, 'assistant', 8081);
    const asBrowser = await probe(9225, 'assistant', 8081);

    expect(asGeminarr.uid, "the probe is Geminarr's user").toBe(1654);
    expect(asBrowser.uid, "the control is the browser's user").toBe(1000);
    expect(asBrowser.open, 'the control: the browser reaches the assistant, as the pod allows').toBe(true);
    expect(asGeminarr.open, "Geminarr's process does not, in the same namespace").toBe(false);
    expect((await probe(9224, '127.0.0.1', 3000)).open, 'it still reaches the browser, on the loopback').toBe(true);
    expect(
      (await probe(9224, '127.0.0.1', 8282)).open,
      'but not another port of the loopback (a proxy of a mesh would be there)',
    ).toBe(false);
  });

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
