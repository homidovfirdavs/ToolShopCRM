import { test, expect } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { T, expectNoHorizontalOverflow, shot, signIn } from "./helpers";

/**
 * Camera barcode scanner on the POS, against the production build with the mocked API.
 * The camera is a FAKE one (Chromium feeds a video file instead of a real camera). The video shows a real,
 * valid EAN-13 barcode (4600100000021 = "Stanley Claw Hammer 450 g" in the mock), so the whole chain is exercised:
 * permission -> video -> decoder -> product lookup -> cart.
 * It does NOT prove that a real phone camera focuses and reads a printed barcode: that is checked by hand.
 */
const FAKE_VIDEO = fileURLToPath(new URL("./fixtures/ean13-4600100000021.mjpeg", import.meta.url));

test.use({
  permissions: ["camera"],
  launchOptions: {
    ...(process.env.E2E_CHROMIUM_PATH ? { executablePath: process.env.E2E_CHROMIUM_PATH } : {}),
    args: [
      "--use-fake-device-for-media-stream",
      "--use-fake-ui-for-media-stream",
      `--use-file-for-fake-video-capture=${FAKE_VIDEO}`,
    ],
  },
});

const HAMMER = "Stanley Claw Hammer 450 g";

test.describe("camera barcode scanner (fake camera)", () => {
  test("a barcode in front of the camera goes to the cart ONCE; no order, no payment, no stock change", async ({ page }, testInfo) => {
    const log = await signIn(page);
    await page.goto("/pos");
    await expect(page.getByTestId("pos-scan-open")).toBeVisible();
    await shot(page, testInfo, "pos-scan-button");

    await page.getByTestId("pos-scan-open").click();
    const dialog = page.getByTestId("scan-dialog");
    await expect(dialog).toBeVisible();
    await expect(page.getByTestId("scan-video")).toBeVisible();

    const feedback = page.getByTestId("scan-feedback");
    await expect(feedback).toContainText(T("pos.scanAdded"), { timeout: 20_000 });
    await expect(feedback).toContainText(HAMMER);
    await expect(feedback).toContainText("35 TJS");

    // The code stays in front of the camera for several more seconds: the quantity must stay 1.
    await page.waitForTimeout(4_000);
    await expect(feedback).toContainText(`${T("pos.scanInCart")}: 1`);
    await expect(page.getByTestId("scan-cart-summary")).toContainText("35 TJS");
    await shot(page, testInfo, "pos-scan-dialog");
    await expectNoHorizontalOverflow(page, "scanner dialog");

    // The scanner only reads the catalogue and stock. It must not have created or completed anything.
    const writes = log.filter((l) => l.method !== "GET");
    expect(writes.map((w) => `${w.method} ${w.path}`)).not.toContain("POST /orders");
    expect(writes.filter((w) => w.path.startsWith("/orders") || w.path.startsWith("/payments") || w.path.startsWith("/inventory"))).toHaveLength(0);
    expect(log.some((l) => /^\/inventory\/stock\/3$/.test(l.path))).toBe(true);

    await page.getByTestId("scan-done").click();
    await expect(dialog).toBeHidden();
    await expect(page.getByTestId("pos-cart-bar")).toContainText("35 TJS");
    await shot(page, testInfo, "pos-scan-cart");
    await expectNoHorizontalOverflow(page, "POS after scan");
  });

  test("camera permission denied: message + manual entry still works", async ({ page, context }) => {
    await context.clearPermissions();
    await signIn(page);
    await page.addInitScript(() => {
      navigator.mediaDevices.getUserMedia = () => Promise.reject(Object.assign(new Error("denied"), { name: "NotAllowedError" }));
    });
    await page.goto("/pos");
    await page.getByTestId("pos-scan-open").click();
    await expect(page.getByTestId("scan-camera-problem")).toContainText(T("pos.scanDenied"));
    await page.getByTestId("scan-manual-input").fill("4600100000021");
    await page.getByTestId("scan-manual-submit").click();
    await expect(page.getByTestId("scan-feedback")).toContainText(T("pos.scanAdded"));
    await expect(page.getByTestId("scan-feedback")).toContainText(HAMMER);
  });

  test("unknown code and out-of-stock product are explained and not added", async ({ page }) => {
    await signIn(page);
    await page.addInitScript(() => {
      navigator.mediaDevices.getUserMedia = () => Promise.reject(Object.assign(new Error("none"), { name: "NotFoundError" }));
    });
    await page.goto("/pos");
    await page.getByTestId("pos-scan-open").click();
    await page.getByTestId("scan-manual-input").fill("9999999999994");
    await page.getByTestId("scan-manual-submit").click();
    await expect(page.getByTestId("scan-feedback")).toContainText(T("pos.scanNotFound"));
    // Product 2 (Bosch Cordless Drill) has stock 0 in the mock.
    await page.getByTestId("scan-manual-input").fill("4600100000014");
    await page.getByTestId("scan-manual-submit").click();
    await expect(page.getByTestId("scan-feedback")).toContainText(T("pos.scanOutOfStock"));
    await expect(page.getByTestId("scan-cart-summary")).toContainText(": 0");
  });
});
