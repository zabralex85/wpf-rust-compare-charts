import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  retries: 0,
  use: { baseURL: "http://localhost:1420", trace: "on-first-retry" },
  expect: { toHaveScreenshot: { maxDiffPixelRatio: 0.02 } },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"], viewport: { width: 1600, height: 900 } } }],
  webServer: { command: "npm run dev", url: "http://localhost:1420", reuseExistingServer: !process.env.CI, timeout: 60_000 },
});
