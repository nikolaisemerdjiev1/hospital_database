import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: './e2e',
  outputDir: './test-results/browser',
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  workers: 2,
  retries: 0,
  timeout: 30_000,
  reporter: 'list',
  use: {
    baseURL: 'http://127.0.0.1:4174',
    browserName: 'chromium',
    serviceWorkers: 'block',
    // No live authentication data or production credentials belong in test artifacts.
    trace: 'off',
    screenshot: 'off',
  },
  webServer: {
    command: 'node node_modules/vite/bin/vite.js --mode browser-test --host 127.0.0.1 --port 4174',
    url: 'http://127.0.0.1:4174',
    reuseExistingServer: false,
    env: {
      VITE_API_BASE_URL: 'http://127.0.0.1:5050',
      VITE_AUTH0_DOMAIN: 'demo-auth.example.invalid',
      VITE_AUTH0_CLIENT_ID: 'browser-test-client',
      VITE_AUTH0_AUDIENCE: 'https://demo-api.example.invalid',
      VITE_DEMO_PASSWORD: 'test-only-demo-value',
    },
  },
})
