import { defineConfig, devices } from '@playwright/test';

// GEMINARR_URL: a running instance (the e2e stack by default). No browser is started by the config.
const baseURL = process.env.GEMINARR_URL ?? 'http://localhost:8282';

export default defineConfig({
  testDir: '.',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: { baseURL, trace: 'on-first-retry' },
  projects: [
    { name: 'chromium', testIgnore: /journey\.spec\.ts/, use: { ...devices['Desktop Chrome'] } },
    // Requests that need no browser give the same answer in each: they run once, in the first project.
    {
      name: 'firefox',
      testIgnore: /journey\.spec\.ts/,
      use: { ...devices['Desktop Firefox'] },
      grepInvert: /@browserless/,
    },
    // The journey changes what the fake assistant shows and what the extension reports (signed in, signed out, disconnected): it runs when the other tests have passed, alone.
    {
      name: 'journey',
      testMatch: /journey\.spec\.ts/,
      dependencies: ['chromium', 'firefox'],
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
