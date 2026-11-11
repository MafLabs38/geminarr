import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

test.describe('smoke @smoke', () => {
  test('the application is alive and ready', { tag: '@browserless' }, async ({ request }) => {
    expect((await request.get('/health/live')).status()).toBe(200);
    expect((await request.get('/health/ready')).status()).toBe(200);
  });

  test(
    'on the real image, an unknown API path or file is a 404 and not the HTML shell',
    { tag: '@browserless' },
    async ({ request }) => {
      for (const path of ['/api/v1/nothing-here', '/missing.js']) {
        const response = await request.get(path);

        expect(response.status(), path).toBe(404);
        expect(response.headers()['content-type'] ?? '', path).not.toContain('text/html');
      }
    },
  );

  test('the overview says the API answers, with no accessibility violation', async ({ page }) => {
    await page.goto('/');

    await expect(page.getByRole('navigation', { name: 'Main menu' })).toBeVisible();
    await expect(page.getByText('API reachable')).toBeVisible();
    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations).toEqual([]);
  });

  test('the system status page shows the version of the running build', async ({ page }) => {
    await page.goto('/system/status');

    await expect(page.getByRole('heading', { name: 'Status' })).toBeVisible();
    await expect(page.getByText('Version')).toBeVisible();
  });

  test('the page is served with its security headers', { tag: '@browserless' }, async ({ request }) => {
    const response = await request.get('/');

    expect(response.headers()['x-content-type-options']).toBe('nosniff');
    expect(response.headers()['content-security-policy']).toContain("frame-ancestors 'none'");
  });
});
