import type React from "react";

interface LineMenuProps {
  x: number;
  y: number;
  onZoomIn: () => void;
  onZoomOut: () => void;
  onReset: () => void;
}

export function LineMenu({ x, y, onZoomIn, onZoomOut, onReset }: LineMenuProps): React.JSX.Element {
  return (
    <div
      data-testid="line-menu"
      className="line-menu"
      style={{ left: x, top: y }}
      onClick={(e) => { e.stopPropagation(); }}
    >
      <div className="line-menu-title">Time axis</div>
      <div className="line-menu-item" onClick={onZoomIn}>
        <span>Zoom in</span><span className="line-menu-key">+</span>
      </div>
      <div className="line-menu-item" onClick={onZoomOut}>
        <span>Zoom out</span><span className="line-menu-key">−</span>
      </div>
      <div className="line-menu-item line-menu-reset" onClick={onReset}>Reset (×1)</div>
    </div>
  );
}
