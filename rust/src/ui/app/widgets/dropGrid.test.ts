import { describe, it, expect } from "vitest";
import { cellFromPoint, resizeStep } from "./dropGrid";

describe("cellFromPoint (pitch 168)", () => {
  it("maps a point in the first cell to (1,1)", () => {
    expect(cellFromPoint({ left: 0, top: 0 }, 10, 10, 0, 0)).toEqual({ col: 1, row: 1 });
  });
  it("maps the 3rd column / 2nd row by pitch", () => {
    expect(cellFromPoint({ left: 0, top: 0 }, 2 * 168 + 5, 1 * 168 + 5, 0, 0)).toEqual({ col: 3, row: 2 });
  });
  it("adds scroll offset and never returns < 1", () => {
    expect(cellFromPoint({ left: 100, top: 100 }, 100, 100, 168, 0)).toEqual({ col: 2, row: 1 });
    expect(cellFromPoint({ left: 500, top: 0 }, 0, 0, 0, 0)).toEqual({ col: 1, row: 1 });
  });
});

describe("resizeStep (pitch 168)", () => {
  it("matches the sample: 24px deadzone then ±1 per pitch", () => {
    expect(resizeStep(0)).toBe(0);
    expect(resizeStep(20)).toBe(0);    // inside 24px deadzone
    expect(resizeStep(24)).toBe(1);    // first step at 24px
    expect(resizeStep(100)).toBe(1);
    expect(resizeStep(150)).toBe(1);
    expect(resizeStep(192)).toBe(2);   // 168 + 24
    expect(resizeStep(-20)).toBe(0);
    expect(resizeStep(-24)).toBe(-1);
    expect(resizeStep(-150)).toBe(-1);
  });
});
