import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import LoginPage from "../pages/LoginPage";
import { renderApp, T } from "./utils";

const login = vi.fn();
const verifyMfa = vi.fn();
const enableMfa = vi.fn();
const startMfaSetup = vi.fn();
const navigate = vi.fn();

vi.mock("../context/AuthContext", () => ({
  useAuth: () => ({ login, verifyMfa, enableMfa }),
}));
vi.mock("../api/mfa", () => ({
  startMfaSetup: (...a: unknown[]) => startMfaSetup(...a),
}));
vi.mock("react-router-dom", async (orig) => ({
  ...(await orig<typeof import("react-router-dom")>()),
  useNavigate: () => navigate,
}));

async function submitPassword(ev: ReturnType<typeof userEvent.setup>) {
  await ev.type(screen.getByLabelText(T("login.username")), "boss");
  await ev.type(screen.getByLabelText(T("login.password")), "a long passphrase here");
  await ev.click(screen.getByRole("button", { name: T("login.submit") }));
}

describe("Login with MFA", () => {
  beforeEach(() => {
    [login, verifyMfa, enableMfa, startMfaSetup, navigate].forEach((m) => m.mockReset());
  });

  it("without MFA the user goes straight in", async () => {
    const ev = userEvent.setup();
    login.mockResolvedValue(null);
    renderApp(<LoginPage />);
    await submitPassword(ev);
    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/"));
    expect(screen.queryByTestId("mfa-verify")).not.toBeInTheDocument();
  });

  it("asks for the code, sends it and enters; a wrong code shows an error and stays", async () => {
    const ev = userEvent.setup();
    login.mockResolvedValue({ mfaToken: "tok", setupRequired: false });
    verifyMfa.mockRejectedValueOnce({ response: { status: 401, data: { message: "no" } } });
    verifyMfa.mockResolvedValueOnce(undefined);
    renderApp(<LoginPage />);
    await submitPassword(ev);

    const code = await screen.findByLabelText(T("login.mfa.code"));
    expect(navigate).not.toHaveBeenCalled();
    await ev.type(code, "12a3456");
    expect(code).toHaveValue("123456"); // digits only, 6 max
    await ev.click(screen.getByRole("button", { name: T("login.mfa.verify") }));
    expect(await screen.findByRole("alert")).toHaveTextContent(T("login.mfa.errCode"));
    expect(verifyMfa).toHaveBeenCalledWith("tok", { code: "123456" });
    expect(navigate).not.toHaveBeenCalled();

    await ev.click(screen.getByRole("button", { name: T("login.mfa.verify") }));
    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/"));
  });

  it("a recovery code can be used instead of the app code; 429 shows the throttle message", async () => {
    const ev = userEvent.setup();
    login.mockResolvedValue({ mfaToken: "tok", setupRequired: false });
    verifyMfa.mockRejectedValueOnce({ response: { status: 429, data: {} } });
    renderApp(<LoginPage />);
    await submitPassword(ev);
    await ev.click(await screen.findByRole("button", { name: T("login.mfa.useRecovery") }));
    await ev.type(screen.getByLabelText(T("login.mfa.recoveryCode")), "abcde-fghij");
    await ev.click(screen.getByRole("button", { name: T("login.mfa.verify") }));
    await waitFor(() => expect(verifyMfa).toHaveBeenCalledWith("tok", { recoveryCode: "abcde-fghij" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(T("login.mfa.errTooMany"));
  });

  it("Back returns to the password form", async () => {
    const ev = userEvent.setup();
    login.mockResolvedValue({ mfaToken: "tok", setupRequired: false });
    renderApp(<LoginPage />);
    await submitPassword(ev);
    await ev.click(await screen.findByRole("button", { name: T("login.mfa.back") }));
    expect(screen.getByLabelText(T("login.username"))).toBeInTheDocument();
    expect(screen.queryByTestId("mfa-verify")).not.toBeInTheDocument();
  });

  it("first-time setup: QR + key, first code, then recovery codes must be confirmed before the session starts", async () => {
    const ev = userEvent.setup();
    const finish = vi.fn();
    login.mockResolvedValue({ mfaToken: "tok", setupRequired: true });
    startMfaSetup.mockResolvedValue({ sharedKey: "ABCD EFGH", otpAuthUri: "otpauth://totp/x?secret=ABCDEFGH" });
    enableMfa.mockResolvedValue({ recoveryCodes: ["aaaaa-bbbbb", "ccccc-ddddd"], finish });
    renderApp(<LoginPage />);
    await submitPassword(ev);

    expect(await screen.findByTestId("mfa-shared-key")).toHaveTextContent("ABCD EFGH");
    expect(startMfaSetup).toHaveBeenCalledWith("tok");
    const enable = screen.getByRole("button", { name: T("login.mfa.enable") });
    expect(enable).toBeDisabled(); // no code yet
    await ev.type(screen.getByLabelText(T("login.mfa.firstCode")), "654321");
    await ev.click(enable);

    await waitFor(() => expect(enableMfa).toHaveBeenCalledWith("tok", "654321"));
    expect(await screen.findByText("aaaaa-bbbbb")).toBeInTheDocument();
    expect(finish).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();

    const cont = screen.getByRole("button", { name: T("login.mfa.continue") });
    expect(cont).toBeDisabled();
    await ev.click(screen.getByLabelText(T("login.mfa.savedConfirm")));
    await ev.click(cont);
    expect(finish).toHaveBeenCalledTimes(1);
    expect(navigate).toHaveBeenCalledWith("/");
  });
});
