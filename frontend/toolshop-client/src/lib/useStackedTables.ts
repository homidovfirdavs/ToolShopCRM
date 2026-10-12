import { useEffect } from "react";

/**
 * Phone layout for data tables.
 *
 * Every <table> inside <main> is marked `data-stack` and each cell gets a `data-label` copied from its
 * column header. On screens under 768px the CSS in index.css (`table[data-stack]`) then draws every row as a
 * card: the first cell is the card title and each other cell is a "Label ........ value" line. Nothing is
 * hidden or removed, so every column (amounts included) stays visible without sideways scrolling.
 * From 768px up nothing changes: the CSS only applies below that width.
 *
 * Opt out for a table that must stay a real grid: <table data-no-stack>.
 * Explicit ARIA roles keep the table semantics for screen readers when the CSS changes the display type.
 */
export function labelTables(root: ParentNode): void {
  root.querySelectorAll<HTMLTableElement>("table:not([data-no-stack])").forEach((table) => {
    if (!table.hasAttribute("data-stack")) {
      table.setAttribute("data-stack", "");
      table.setAttribute("role", "table");
    }

    const headRow = table.tHead?.rows[0];
    const labels: string[] = [];
    if (headRow) {
      Array.from(headRow.cells).forEach((th) => {
        const text = (th.textContent ?? "").replace(/[▲▼↑↓]/g, "").replace(/\s+/g, " ").trim();
        for (let i = 0; i < Math.max(1, th.colSpan); i++) labels.push(text);
      });
    }

    Array.from(table.rows).forEach((row) => {
      if (row.parentElement?.tagName === "THEAD") return;
      if (row.getAttribute("role") !== "row") row.setAttribute("role", "row");
      let col = 0;
      Array.from(row.cells).forEach((cell) => {
        if (cell.getAttribute("role") !== "cell") cell.setAttribute("role", "cell");
        const label = labels[col] ?? "";
        if (cell.getAttribute("data-label") !== label) cell.setAttribute("data-label", label);
        col += Math.max(1, cell.colSpan);
      });
    });
  });
}

export function useStackedTables(): void {
  useEffect(() => {
    const main = document.querySelector("main");
    if (!main) return;
    let frame = 0;
    const run = () => {
      frame = 0;
      labelTables(main);
    };
    const schedule = () => {
      if (!frame) frame = window.requestAnimationFrame(run);
    };
    labelTables(main);
    // Only child-list changes are observed (rows added/removed, page changes); setting attributes does not retrigger it.
    const observer = new MutationObserver(schedule);
    observer.observe(main, { childList: true, subtree: true });
    return () => {
      observer.disconnect();
      if (frame) window.cancelAnimationFrame(frame);
    };
  }, []);
}
