// rust/e2e/shell.spec.ts
import { test, expect } from "@playwright/test";

test.beforeEach(async ({ page }) => { await page.goto("/?mock=1"); });

test("top bar + tabs + param panel render the mock data", async ({ page }) => {
  await expect(page.getByText("INU·MONITOR")).toBeVisible();
  // param panel groups + a channel
  await expect(page.getByText("PARAMETERS")).toBeVisible();
  await expect(page.getByText("Attitude")).toBeVisible();
  await expect(page.getByText("Roll", { exact: false }).first()).toBeVisible();
  // tab switching
  await page.getByText("EVENTS", { exact: true }).click();
  await expect(page.getByTestId("view-events")).toBeVisible();
  await page.getByText("OVERVIEW", { exact: true }).click();
  await expect(page.getByTestId("view-overview")).toBeVisible();
});

test("overview matches the visual baseline", async ({ page }) => {
  // let fonts settle
  await page.evaluate(() => document.fonts.ready);
  await expect(page).toHaveScreenshot("overview.png", { fullPage: false });
});
