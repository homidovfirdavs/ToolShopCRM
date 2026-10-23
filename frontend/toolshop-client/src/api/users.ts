import apiClient from "./client";

export type UserRole = "Admin" | "Manager" | "Cashier" | "Warehouse";

export const USER_ROLES: UserRole[] = ["Admin", "Manager", "Cashier", "Warehouse"];

export interface ManagedUser {
  id: string;
  username: string;
  fullName: string;
  email: string;
  role: string;
  isActive: boolean;
  isLockedOut: boolean;
  mfaEnabled: boolean;
  createdAt: string;
}

export interface CreateUserValues {
  username: string;
  fullName: string;
  email: string;
  password: string;
  role: UserRole;
}

export interface UpdateUserValues {
  fullName: string;
  email: string;
  role: UserRole;
}

export async function getUsers(): Promise<ManagedUser[]> {
  const res = await apiClient.get("/users");
  return res.data;
}

export async function createUser(values: CreateUserValues): Promise<ManagedUser> {
  const res = await apiClient.post("/users", values);
  return res.data;
}

export async function updateUser(id: string, values: UpdateUserValues): Promise<ManagedUser> {
  const res = await apiClient.put(`/users/${id}`, values);
  return res.data;
}

export async function deactivateUser(id: string): Promise<ManagedUser> {
  const res = await apiClient.post(`/users/${id}/deactivate`);
  return res.data;
}

export async function activateUser(id: string): Promise<ManagedUser> {
  const res = await apiClient.post(`/users/${id}/activate`);
  return res.data;
}

export async function resetUserPassword(id: string, newPassword: string): Promise<void> {
  await apiClient.post(`/users/${id}/reset-password`, { newPassword });
}

export async function resetUserMfa(id: string): Promise<void> {
  await apiClient.post(`/users/${id}/mfa/reset`);
}
