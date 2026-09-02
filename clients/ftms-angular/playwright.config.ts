import { defineConfig, devices } from '@playwright/test';

/**
 * design: doc 08 section 5 - Playwright covers the critical journeys only, and the suite stays
 * small deliberately, because e2e suites that chase coverage become the slowest, flakiest thing
 * in CI. The unit and integration bands below it are where breadth belongs.
 *
 * The full journey runs against a real API backed by a real database, so it is a true end to
 * end check rather than a mocked one. Start both first:
 *
 *   dotnet run --project src/FTMS.Api
 *   npm start
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,

  // A test that only passes on a retry is a flaky test, and a flaky test in a financial
  // pipeline trains people to ignore red. Retry once in CI to absorb genuine infrastructure
  // blips, never locally, where the blip should be investigated.
  retries: process.env['CI'] ? 1 : 0,

  // A stray .only would silently reduce the suite to one test and still report green.
  forbidOnly: !!process.env['CI'],
  workers: process.env['CI'] ? 1 : undefined,

  // The json report exists so CI can assert that the journeys actually RAN. This suite spent a
  // long time as test.skip(true, ...) and a green tick said nothing about it either way; a skip
  // and a pass look identical in a summary line. The integration suite has the same guard
  // against outcome="NotExecuted" for the same reason.
  reporter: process.env['CI']
    ? [
        ['list'],
        ['html', { open: 'never' }],
        ['json', { outputFile: 'playwright-report/results.json' }],
      ]
    : 'list',

  use: {
    baseURL: process.env['FTMS_E2E_BASE_URL'] ?? 'http://localhost:4200',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  // Starts the dev server, which proxies /api to the backend. The API itself is NOT started
  // here: it needs a database, and a web server config that silently skips migrations would
  // make failures hard to read.
  webServer: {
    command: 'npm start',
    url: 'http://localhost:4200',
    reuseExistingServer: !process.env['CI'],
    timeout: 120_000,
  },
});
