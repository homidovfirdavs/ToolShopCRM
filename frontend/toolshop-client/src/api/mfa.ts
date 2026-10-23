import apiClient from "./client";

export interface MfaSetupData {
  sharedKey: string;
  otpAuthUri: string;
}

/** Second step of the login: the API answered the password with this instead of tokens. */
export interface MfaChallenge {
  mfaToken: string;
  /** true: the user has no authenticator yet and must set one up first. */
  setupRequired: boolean;
}

export interface MfaCodeInput {
  code?: string;
  recoveryCode?: string;
}

export async function startMfaSetup(mfaToken: string): Promise<MfaSetupData> {
  const res = await apiClient.post("/auth/mfa/setup", { mfaToken });
  return res.data;
}

export async function submitMfaEnable(mfaToken: string, code: string) {
  const res = await apiClient.post("/auth/mfa/enable", { mfaToken, code });
  return res.data;
}

export async function submitMfaVerify(mfaToken: string, input: MfaCodeInput) {
  const res = await apiClient.post("/auth/mfa/verify", { mfaToken, ...input });
  return res.data;
}
