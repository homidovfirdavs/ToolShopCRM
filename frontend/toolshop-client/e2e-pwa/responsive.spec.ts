import { test, expect } from "@playwright/test";
import { T, expectNoHorizontalOverflow, isDrawerLayout, isTouchLayout, shot, signIn } from "./helpers";

const ROUTES = [
  "/dashboard", "/pos", "/orders", "/products", "/inventory", "/inventory/movements", "/finance", "/reports",
  "/customers", "/payments", "/debts", "/expenses", "/categories", "/brands", "/suppliers", "/purchases", "/users",
];

test.describe("pages fit the screen", () => {
  for (const route of ROUTES) {
    test(`${route}: no horizontal page overflow`, async ({ page }, testInfo) => {
      await signIn(page);
      await page.goto(route);
      await expect(page.locator("h1").first()).toBeVisible();
      await page.waitForLoadState("networkidle");
      await page.waitForTimeout(700); // chart animations
      await expectNoHorizontalOverflow(page, route);
      await shot(page, testInfo, route.slice(1).replace(/\//g, "-") || "home");
    });
  }
});

test.describe("lists adapt to the screen", () => {
  const LISTS: [string, string][] = [
    ["/orders", "orders-cards"],
    ["/inventory", "inventory-cards"],
    ["/products", "products-cards"],
    ["/customers", "customers-cards"],
  ];
  for (const [route, cardsId] of LISTS) {
    test(`${route}: cards below 768px, table from 768px`, async ({ page }, testInfo) => {
      await signIn(page);
      await page.goto(route);
      await page.waitForLoadState("networkidle");
      const phone = (page.viewportSize()?.width ?? 1280) < 768;
      const cards = page.getByTestId(cardsId);
      const table = page.locator("table").first();
      if (phone) {
        await expect(cards).toBeVisible();
        await expect(table).toBeHidden();
        expect(await cards.locator("li").count()).toBeGreaterThan(2);
        await shot(page, testInfo, `cards-${cardsId}`);
      } else {
        await expect(table).toBeVisible();
        await expect(cards).toBeHidden();
      }
    });
  }
});

test.describe("navigation", () => {
  test("sidebar is a mobile drawer below 1024px and a fixed column above", async ({ page }, testInfo) => {
    await signIn(page);
    await page.goto("/orders");
    const menu = page.getByRole("button", { name: T("common.menu") });
    const nav = page.getByRole("navigation", { name: "Main" });
    if (isDrawerLayout(page)) {
      await expect(menu).toBeVisible();
      await expect(nav).toBeHidden();
      await menu.click();
      await expect(page.getByRole("dialog")).toBeVisible();
      await expect(nav).toBeVisible();
      await shot(page, testInfo, "drawer-open");
      // body behind the drawer must not scroll
      expect(await page.evaluate(() => document.body.style.overflow)).toBe("hidden");
      await nav.getByRole("link", { name: T("nav.products") }).click();
      await expect(page).toHaveURL(/\/products$/);
      await expect(page.getByRole("dialog")).toHaveCount(0); // drawer closed after navigating
      expect(await page.evaluate(() => document.body.style.overflow)).toBe("");
    } else {
      await expect(menu).toBeHidden();
      await expect(nav).toBeVisible();
    }
  });

  test("drawer links and header controls are at least 44px tall on touch layouts", async ({ page }) => {
    test.skip(!isDrawerLayout(page), "desktop layout");
    await signIn(page);
    await page.goto("/orders");
    await page.getByRole("button", { name: T("common.menu") }).click();
    const links = page.getByRole("navigation", { name: "Main" }).getByRole("link");
    const n = await links.count();
    expect(n).toBeGreaterThan(8);
    for (let i = 0; i < n; i++) {
      const box = await links.nth(i).boundingBox();
      expect(box!.height, `nav link #${i}`).toBeGreaterThanOrEqual(43.5);
    }
  });
});

test.describe("POS on touch devices", () => {
  test("cart bar, cart visibility and 44px touch targets", async ({ page }, testInfo) => {
    test.skip(!isTouchLayout(page), "desktop layout keeps the cart in the right column");
    await signIn(page);
    await page.goto("/pos");
    await expect(page.getByText("Makita Rotary Hammer HR2470").first()).toBeVisible();
    await shot(page, testInfo, "pos-empty");
    await expect(page.getByTestId("pos-cart-bar")).toHaveCount(0);

    await page.getByRole("button", { name: /Makita Rotary Hammer/ }).first().click();
    await page.getByRole("button", { name: /Stanley Claw Hammer/ }).first().click();
    const bar = page.getByTestId("pos-cart-bar");
    await expect(bar).toBeVisible();
    await expect(bar).toContainText("435 TJS");
    await shot(page, testInfo, "pos-with-cart-bar");

    // touch targets inside the cart
    for (const name of ["−", "+"]) {
      const box = await page.getByRole("button", { name, exact: true }).first().boundingBox();
      expect(box!.width, `${name} width`).toBeGreaterThanOrEqual(43.5);
      expect(box!.height, `${name} height`).toBeGreaterThanOrEqual(43.5);
    }

    // the bar brings the cart + payment into view
    await bar.getByRole("button", { name: T("pos.goToCart") }).click();
    await expect.poll(async () => (await page.locator("#pos-cart").boundingBox())!.y, { timeout: 5000 }).toBeLessThan(160);
    await expectNoHorizontalOverflow(page, "pos cart");
    await shot(page, testInfo, "pos-cart-section");

    // the cart is usable: the quantity changes the total
    await page.getByRole("button", { name: "+", exact: true }).first().click();
    await expect(page.getByTestId("pos-cart-bar")).toContainText("835 TJS");
  });
});

test.describe("roles and permissions are unchanged", () => {
  test("a cashier sees only cashier navigation and is kept out of finance pages", async ({ page }) => {
    await signIn(page, { role: "cashier" });
    if (isDrawerLayout(page)) await page.getByRole("button", { name: T("common.menu") }).click();
    const nav = page.getByRole("navigation", { name: "Main" });
    await expect(nav.getByRole("link", { name: T("nav.pos") })).toBeVisible();
    await expect(nav.getByRole("link", { name: T("nav.orders") })).toBeVisible();
    for (const key of ["nav.finance", "nav.reports", "nav.users", "nav.expenses", "nav.dashboard"]) {
      await expect(nav.getByRole("link", { name: T(key) })).toHaveCount(0);
    }
    await page.goto("/finance");
    await expect(page).not.toHaveURL(/\/finance$/);
    await page.goto("/users");
    await expect(page).not.toHaveURL(/\/users$/);
  });
});

test.describe("phone: every table column stays reachable", () => {
  for (const [route, amountText] of [["/debts", "5 200 TJS"], ["/expenses", "800 TJS"]] as const) {
    test(`${route}: rows are cards and the amount is inside the screen`, async ({ page }, testInfo) => {
      test.skip((page.viewportSize()?.width ?? 1280) >= 768, "tables are tables from 768px");
      await signIn(page);
      await page.goto(route);
      await page.waitForLoadState("networkidle");
      await expect(page.locator("table[data-stack]").first()).toBeAttached();
      await expectNoHorizontalOverflow(page, route);
      if (amountText) {
        const cell = page.locator("td", { hasText: amountText.replace(/ /g, "\u00a0") }).first();
        const alt = page.locator("td", { hasText: amountText }).first();
        const target = (await cell.count()) ? cell : alt;
        await expect(target).toBeVisible();
        const box = await target.boundingBox();
        const width = page.viewportSize()!.width;
        expect(box!.x, "amount starts inside the screen").toBeGreaterThanOrEqual(0);
        expect(box!.x + box!.width, "amount ends inside the screen").toBeLessThanOrEqual(width + 1);
      }
      await shot(page, testInfo, `stacked${route.replace("/", "-")}`);
    });
  }
});
