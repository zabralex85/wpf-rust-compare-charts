import { test, expect } from "@playwright/test";
test.beforeEach(async ({ page }) => { await page.goto("/?mock=1"); });

test("map shows geo chrome + an OSM toggle", async ({ page }) => {
  const map = page.getByTestId("mapwidget");
  await expect(map).toBeVisible();
  await expect(map.getByText("N↑")).toBeVisible();
  await expect(map.getByText(/°N .*°E/)).toBeVisible();
  await expect(map.getByText("2 km")).toBeVisible();
  await expect(map.getByText("OSM MAP")).toBeVisible();
});

test("map (grid mode) matches the visual baseline", async ({ page }) => {
  await page.evaluate(() => document.fonts.ready);
  await expect(page.getByTestId("mapwidget")).toHaveScreenshot("map.png");
});
