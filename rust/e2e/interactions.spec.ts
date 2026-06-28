// rust/e2e/interactions.spec.ts
import { test, expect } from "@playwright/test";

test.beforeEach(async ({ page }) => { await page.goto("/?mock=1"); });

test("toggle a gauge to a line, then remove it", async ({ page }) => {
  const cell = page.locator("[data-widget]").filter({ has: page.getByTestId("gauge") }).first();
  // Capture the widget's stable data-widget id BEFORE clicking toggle.
  // Playwright locators re-evaluate lazily: after the click the filter
  // `has: gauge` no longer matches this cell (it now holds a linechart),
  // so the locator would drift to the next gauge cell.  Pinning by id is
  // the reliable pattern.
  const widgetId = await cell.getAttribute("data-widget");
  await cell.locator('[data-act="toggle"]').click();
  // Use the stable id-based locator for post-toggle assertions.
  const toggled = page.locator(`[data-widget="${widgetId}"]`);
  await expect(toggled.getByTestId("linechart")).toBeVisible();
  // After toggle the cell expands (cols: 1→2) and can overlap the adjacent
  // gauge cell, which intercepts pointer events at the × button's coordinate.
  // dispatchEvent bypasses hit-testing and fires directly on the target.
  await toggled.locator('[data-act="remove"]').dispatchEvent("click");
  await expect(toggled).toHaveCount(0);
});

// Playwright's dragTo fires synthetic mouse events (mousedown → mousemove → mouseup).
// The ParamPanel uses the HTML5 Drag-and-Drop API (dragstart / dragover / drop) with
// dataTransfer payloads, which Chromium does NOT fire in response to mouse events alone.
// dispatchEvent-based approach below synthesises the full DnD sequence manually.
test("drag a parameter onto the grid adds a gauge", async ({ page }) => {
  const before = await page.getByTestId("gauge").count();

  // Get bounding boxes for source row and drop zone
  const row = page.locator("[data-prow]").first();
  const dz = page.locator("[data-dropzone]");
  const rowBox = await row.boundingBox();
  const dzBox = await dz.boundingBox();
  if (!rowBox || !dzBox) throw new Error("Could not get bounding boxes");

  // Synthesise HTML5 DnD: dragstart on the param row → dragover → drop on the dropzone
  await page.evaluate(
    ({ rx, ry, dx, dy }: { rx: number; ry: number; dx: number; dy: number }) => {
      const src = document.elementFromPoint(rx, ry) as HTMLElement | null;
      const target = document.elementFromPoint(dx, dy) as HTMLElement | null;
      if (!src || !target) throw new Error("elements not found at coordinates");

      // Find the actual [data-prow] ancestor
      const prow = src.closest("[data-prow]") as HTMLElement | null;
      const dropzone = target.closest("[data-dropzone]") as HTMLElement | null;
      if (!prow || !dropzone) throw new Error("[data-prow] or [data-dropzone] not found");

      const dt = new DataTransfer();
      // Replicate the payload set in ParamPanel onDragStart
      // We read the channel id from the data-prow attribute (number, matching production)
      const channelId = Number(prow.dataset["prow"]!);
      const nameEl = prow.querySelector(".param-name");
      const unitEl = prow.querySelector(".param-unit");
      const name = nameEl?.textContent ?? channelId;
      const unit = unitEl?.textContent ?? "";
      const payload = JSON.stringify({ channelId, name, unit });
      dt.setData("application/x-inu-param", payload);
      dt.setData("text/plain", payload);

      prow.dispatchEvent(new DragEvent("dragstart", { bubbles: true, cancelable: true, dataTransfer: dt }));
      dropzone.dispatchEvent(new DragEvent("dragover", { bubbles: true, cancelable: true, dataTransfer: dt, clientX: dx, clientY: dy }));
      dropzone.dispatchEvent(new DragEvent("drop", { bubbles: true, cancelable: true, dataTransfer: dt, clientX: dx, clientY: dy }));
      prow.dispatchEvent(new DragEvent("dragend", { bubbles: true, cancelable: true, dataTransfer: dt }));
    },
    {
      rx: rowBox.x + rowBox.width / 2,
      ry: rowBox.y + rowBox.height / 2,
      dx: dzBox.x + 30,
      dy: dzBox.y + 30,
    }
  );

  await expect(page.getByTestId("gauge")).toHaveCount(before + 1);
});

test("editable grid matches the visual baseline", async ({ page }) => {
  // Wait for all widget types to be present (same pattern as widgets.spec.ts).
  await expect(page.getByTestId("gauge").first()).toBeVisible();
  await expect(page.getByTestId("linechart").first()).toBeVisible();
  await expect(page.getByTestId("mapwidget")).toBeVisible();
  await page.evaluate(() => document.fonts.ready);
  await expect(page.getByTestId("overview-dash")).toHaveScreenshot("interactions.png");
});
