import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, type PlaywrightWorkerArgs, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';

/** The operator of the e2e stack (tests/e2e/stack/docker-compose.yml): a test-only value. */
const OPERATOR = { name: 'e2e-operator', password: 'e2e-admin-password' };

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
