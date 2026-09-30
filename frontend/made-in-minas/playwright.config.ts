import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  workers: process.env['CI'] ? 2 : 1,
  forbidOnly: !!process.env['CI'],
  reporter: 'list',
  use: {
    baseURL: 'http://localhost:8101',
    channel: process.env['PLAYWRIGHT_CHANNEL'] || (process.env['CI'] ? undefined : 'msedge'),
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'desktop', use: { viewport: { width: 1280, height: 900 } } },
    { name: 'mobile', use: { ...devices['iPhone 13'], defaultBrowserType: 'chromium' } },
  ],
  webServer: {
    command: 'npm run start',
    url: 'http://localhost:8101',
    reuseExistingServer: !process.env['CI'],
    timeout: 120000,
  },
});
