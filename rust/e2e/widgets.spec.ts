// rust/e2e/widgets.spec.ts
import { test, expect } from "@playwright/test";

test.beforeEach(async ({ page }) => { await page.goto("/?mock=1"); });

test("widget grid: gauge, linechart, and mapwidget are visible", async ({ page }) => {
  // mapwidget
  await expect(page.getByTestId("mapwidget")).toBeVisible();
  // at least one gauge
  await expect(page.getByTestId("gauge").first()).toBeVisible();
  // at least one linechart
  await expect(page.getByTestId("linechart").first()).toBeVisible();
});

test("widget grid matches the visual baseline", async ({ page }) => {
  // Wait for all widgets to be present before capturing
  await expect(page.getByTestId("gauge").first()).toBeVisible();
  await expect(page.getByTestId("linechart").first()).toBeVisible();
  await expect(page.getByTestId("mapwidget")).toBeVisible();
  // Let fonts settle
  await page.evaluate(() => document.fonts.ready);
  // Screenshot just the dashboard column for stability
  await expect(page.getByTestId("overview-dash")).toHaveScreenshot("widgets.png");
});
