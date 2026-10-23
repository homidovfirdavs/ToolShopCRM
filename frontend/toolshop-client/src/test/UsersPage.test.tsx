import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import UsersPage from "../pages/UsersPage";
import { isStrongPassword } from "../lib/password";
import { navGroups } from "../components/layout/navConfig";
import { renderApp, T } from "./utils";

const getUsers = vi.fn();
const createUser = vi.fn();
const updateUser = vi.fn();
const deactivateUser = vi.fn();
const activateUser = vi.fn();
const resetUserPassword = vi.fn();

vi.mock("../api/users", () => ({
  USER_ROLES: ["Admin", "Manager", "Cashier", "Warehouse"],
  getUsers: (...a: unknown[]) => getUsers(...a),
  createUser: (...a: unknown[]) => createUser(...a),
  updateUser: (...a: unknown[]) => updateUser(...a),
  deactivateUser: (...a: unknown[]) => deactivateUser(...a),
  activateUser: (...a: unknown[]) => activateUser(...a),
  resetUserPassword: (...a: unknown[]) => resetUserPassword(...a),
}));
vi.mock("../context/AuthContext", () => ({
  useAuth: () => ({ user: { id: "me", username: "admin", fullName: "Admin", email: "a@x", roles: ["Admin"] } }),
}));

const user = (over: Record<string, unknown> = {}) => ({
  id: "u1",
  username: "cashier1",
  fullName: "Cash Ier",
  email: "c@x.tj",
  role: "Cashier",
  isActive: true,
  isLockedOut: false,
  createdAt: "2026-10-01T10:00:00Z",
  ...over,
});

const list = () => [
  user({ id: "me", username: "admin", fullName: "Admin", role: "Admin" }),
  user(),
  user({ id: "u2", username: "old1", fullName: "Old One", role: "Warehouse", isActive: false }),
];

const row = (username: string) => screen.getByTestId(`user-row-${username}`);

describe("UsersPage", () => {
  beforeEach(() => {
    [getUsers, createUser, updateUser, deactivateUser, activateUser, resetUserPassword].forEach((m) => m.mockReset());
    getUsers.mockResolvedValue(list());
  });

  it("lists users with role and status; the current user cannot deactivate themselves", async () => {
    renderApp(<UsersPage />);
    expect(await screen.findByText("cashier1")).toBeInTheDocument();
    expect(within(row("cashier1")).getByText(T("users.role.Cashier"))).toBeInTheDocument();
    expect(within(row("old1")).getByText(T("common.inactive"))).toBeInTheDocument();
    expect(within(row("admin")).getByText(`(${T("users.you")})`)).toBeInTheDocument();
    expect(within(row("admin")).queryByRole("button", { name: T("common.deactivate") })).not.toBeInTheDocument();
    expect(within(row("cashier1")).getByRole("button", { name: T("common.deactivate") })).toBeInTheDocument();
  });

  it("shows empty and error states", async () => {
    getUsers.mockResolvedValueOnce([]);
    const { unmount } = renderApp(<UsersPage />);
    expect(await screen.findByText(T("users.empty"))).toBeInTheDocument();
    unmount();
    getUsers.mockRejectedValueOnce(new Error("boom"));
    renderApp(<UsersPage />);
    expect(await screen.findByText(T("common.loadFailed"))).toBeInTheDocument();
  });

  it("create: weak password is rejected locally, a valid form calls the API", async () => {
    const ev = userEvent.setup();
    createUser.mockResolvedValue(user({ id: "n1", username: "newbie" }));
    renderApp(<UsersPage />);
    await screen.findByText("cashier1");
    await ev.click(screen.getByRole("button", { name: `+ ${T("users.add")}` }));

    const dialog = screen.getByRole("dialog");
    await ev.type(within(dialog).getByLabelText(new RegExp(`^${T("users.username")}`)), "newbie");
    await ev.type(within(dialog).getByLabelText(new RegExp(`^${T("users.fullName")}`)), "New Bie");
    await ev.type(within(dialog).getByLabelText(new RegExp(`^${T("users.email")}`)), "n@x.tj");
    await ev.type(within(dialog).getByLabelText(new RegExp(`^${T("users.password")}`)), "weak");
    await ev.click(within(dialog).getByRole("button", { name: T("common.save") }));
    expect(within(dialog).getByRole("alert")).toHaveTextContent(T("users.validation.password"));
    expect(createUser).not.toHaveBeenCalled();

    const pw = within(dialog).getByLabelText(new RegExp(`^${T("users.password")}`));
    await ev.clear(pw);
    await ev.type(pw, "Strong-Pass1");
    await ev.selectOptions(within(dialog).getByLabelText(new RegExp(`^${T("users.role")}`)), "Manager");
    await ev.click(within(dialog).getByRole("button", { name: T("common.save") }));

    await waitFor(() => expect(createUser).toHaveBeenCalledTimes(1));
    expect(createUser).toHaveBeenCalledWith({
      username: "newbie",
      fullName: "New Bie",
      email: "n@x.tj",
      password: "Strong-Pass1",
      role: "Manager",
    });
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(getUsers).toHaveBeenCalledTimes(2); // reloaded
  });

  it("create: maps a server error code to a translated message and keeps the dialog open", async () => {
    const ev = userEvent.setup();
    createUser.mockRejectedValue({ response: { status: 409, data: { code: "USERNAME_TAKEN", message: "x" } } });
    renderApp(<UsersPage />);
    await screen.findByText("cashier1");
    await ev.click(screen.getByRole("button", { name: `+ ${T("users.add")}` }));
    const dialog = screen.getByRole("dialog");
    await ev.type(within(dialog).getByLabelText(new RegExp(`^${T("users.username")}`)), "cashier1");
    await ev.type(within(dialog).getByLabelText(new RegExp(`^${T("users.fullName")}`)), "Dup");
    await ev.type(within(dialog).getByLabelText(new RegExp(`^${T("users.email")}`)), "d@x.tj");
    await ev.type(within(dialog).getByLabelText(new RegExp(`^${T("users.password")}`)), "Strong-Pass1");
    await ev.click(within(dialog).getByRole("button", { name: T("common.save") }));
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(T("users.err.USERNAME_TAKEN"));
  });

  it("deactivate asks for confirmation and then calls the API", async () => {
    const ev = userEvent.setup();
    deactivateUser.mockResolvedValue(user({ isActive: false }));
    renderApp(<UsersPage />);
    await screen.findByText("cashier1");
    await ev.click(within(row("cashier1")).getByRole("button", { name: T("common.deactivate") }));
    expect(deactivateUser).not.toHaveBeenCalled();
    const dialog = screen.getByRole("dialog");
    await ev.click(within(dialog).getByRole("button", { name: T("common.deactivate") }));
    await waitFor(() => expect(deactivateUser).toHaveBeenCalledWith("u1"));
  });

  it("deactivate failure for the last admin shows the translated reason", async () => {
    const ev = userEvent.setup();
    deactivateUser.mockRejectedValue({ response: { status: 409, data: { code: "LAST_ADMIN", message: "x" } } });
    renderApp(<UsersPage />);
    await screen.findByText("cashier1");
    await ev.click(within(row("cashier1")).getByRole("button", { name: T("common.deactivate") }));
    await ev.click(within(screen.getByRole("dialog")).getByRole("button", { name: T("common.deactivate") }));
    expect(await screen.findByText(T("users.err.LAST_ADMIN"))).toBeInTheDocument();
  });

  it("an inactive user can be activated again", async () => {
    const ev = userEvent.setup();
    activateUser.mockResolvedValue(user({ id: "u2", isActive: true }));
    renderApp(<UsersPage />);
    await screen.findByText("old1");
    await ev.click(within(row("old1")).getByRole("button", { name: T("common.activate") }));
    await waitFor(() => expect(activateUser).toHaveBeenCalledWith("u2"));
  });

  it("reset password validates strength and sends the new password", async () => {
    const ev = userEvent.setup();
    resetUserPassword.mockResolvedValue(undefined);
    renderApp(<UsersPage />);
    await screen.findByText("cashier1");
    await ev.click(within(row("cashier1")).getByRole("button", { name: T("users.resetPassword") }));
    const dialog = screen.getByRole("dialog");
    const input = within(dialog).getByLabelText(new RegExp(`^${T("users.newPassword")}`));
    await ev.type(input, "abc");
    await ev.click(within(dialog).getByRole("button", { name: T("common.save") }));
    expect(resetUserPassword).not.toHaveBeenCalled();
    await ev.clear(input);
    await ev.type(input, "Another-Pass2");
    await ev.click(within(dialog).getByRole("button", { name: T("common.save") }));
    await waitFor(() => expect(resetUserPassword).toHaveBeenCalledWith("u1", "Another-Pass2"));
  });
});

describe("password rule and navigation", () => {
  it("isStrongPassword only checks the length (12..128); the server rejects obvious passwords", () => {
    expect(isStrongPassword("blue river walks past quiet hills")).toBe(true);   // phrase: no digits, no capitals
    expect(isStrongPassword("abcdefghijkl")).toBe(true);                         // exactly 12
    expect(isStrongPassword("Abcdef1-xyz")).toBe(false);                         // 11 characters
    expect(isStrongPassword("Ab1")).toBe(false);
    expect(isStrongPassword("a".repeat(128))).toBe(true);
    expect(isStrongPassword("a".repeat(129))).toBe(false);
  });

  it("the Users link requires the Users.Manage permission", () => {
    const item = navGroups.flatMap((g) => g.items).find((i) => i.to === "/users");
    expect(item?.permission).toBe("Users.Manage");
  });
});
