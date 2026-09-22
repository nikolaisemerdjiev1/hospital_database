import { defineConfig } from '@playwright/test'

// Only the isolated container built with fake Auth0/password settings is tested here.
export default defineConfig({
  testDir: './container-e2e',
  outputDir: './test-results/container',
  workers: 1,
  retries: 0,
  forbidOnly: Boolean(process.env.CI),
  timeout: 70_000,
  reporter: 'list',
  use: {
    baseURL: 'http://127.0.0.1:8086',
    browserName: 'chromium',
    serviceWorkers: 'block',
    trace: 'off',
    screenshot: 'off',
  },
})
