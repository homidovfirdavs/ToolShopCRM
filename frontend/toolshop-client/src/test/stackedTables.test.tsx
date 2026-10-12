import { describe, expect, it } from "vitest";
import { labelTables } from "../lib/useStackedTables";

function mount(html: string): HTMLElement {
  const root = document.createElement("main");
  root.innerHTML = html;
  return root;
}

describe("labelTables (phone: table rows become cards)", () => {
  it("copies each column header into a data-label on its cells, ignoring sort arrows", () => {
    const root = mount(`
      <table>
        <thead><tr><th>Customer</th><th>Total debt ▼</th><th></th></tr></thead>
        <tbody><tr><td>Acme</td><td>5 200 TJS</td><td><button>View</button></td></tr></tbody>
      </table>`);
    labelTables(root);
    const table = root.querySelector("table")!;
    expect(table.hasAttribute("data-stack")).toBe(true);
    const cells = Array.from(root.querySelectorAll("tbody td"));
    expect(cells.map((c) => c.getAttribute("data-label"))).toEqual(["Customer", "Total debt", ""]);
    expect(root.querySelector("tbody tr")!.getAttribute("role")).toBe("row");
  });

  it("does not change cell text or remove any column (nothing is hidden by script)", () => {
    const root = mount(`<table><thead><tr><th>A</th><th>B</th></tr></thead><tbody><tr><td>1</td><td>2</td></tr></tbody></table>`);
    labelTables(root);
    expect(Array.from(root.querySelectorAll("tbody td")).map((c) => c.textContent)).toEqual(["1", "2"]);
  });

  it("skips tables marked data-no-stack", () => {
    const root = mount(`<table data-no-stack><thead><tr><th>A</th></tr></thead><tbody><tr><td>1</td></tr></tbody></table>`);
    labelTables(root);
    expect(root.querySelector("table")!.hasAttribute("data-stack")).toBe(false);
    expect(root.querySelector("td")!.hasAttribute("data-label")).toBe(false);
  });

  it("labels rows added later and handles colspan", () => {
    const root = mount(`<table><thead><tr><th colspan="2">Name</th><th>Qty</th></tr></thead><tbody><tr><td>a</td><td>b</td><td>3</td></tr></tbody></table>`);
    labelTables(root);
    const tbody = root.querySelector("tbody")!;
    tbody.insertAdjacentHTML("beforeend", "<tr><td>c</td><td>d</td><td>4</td></tr>");
    labelTables(root);
    const last = Array.from(tbody.rows[1].cells).map((c) => c.getAttribute("data-label"));
    expect(last).toEqual(["Name", "Name", "Qty"]);
  });
});
