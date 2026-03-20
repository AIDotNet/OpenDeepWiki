import { getApiProxyUrl } from "./env";
import { getToken, UserInfo, ApiResponse } from "./auth-api";

function getApiBaseUrl(): string {
  return getApiProxyUrl();
}

function buildApiUrl(path: string) {
  const baseUrl = getApiBaseUrl();
  if (!baseUrl) {
    return path;
  }
  const trimmedBase = baseUrl.endsWith("/") ? baseUrl.slice(0, -1) : baseUrl;
  return `${trimmedBase}${path}`;
}

export interface UpdateProfileRequest {
  name: string;
  email: string;
  phone?: string;
  avatar?: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

export interface UserSettings {
  theme: "light" | "dark" | "system";
  language: string;
  emailNotifications: boolean;
  pushNotifications: boolean;
}

export async function updateProfile(request: UpdateProfileRequest): Promise<UserInfo> {
  const token = getToken();
  if (!token) throw new Error("Not signed in");

  const url = buildApiUrl("/api/user/profile");
  const response = await fetch(url, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.message || "Update failed");
  }

  const result = (await response.json()) as ApiResponse<UserInfo>;
  if (!result.success || !result.data) {
    throw new Error(result.message || "Update failed");
  }

  return result.data;
}

export async function changePassword(request: ChangePasswordRequest): Promise<void> {
  const token = getToken();
  if (!token) throw new Error("Not signed in");

  const url = buildApiUrl("/api/user/password");
  const response = await fetch(url, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.message || "Password change failed");
  }

  const result = (await response.json()) as ApiResponse<void>;
  if (!result.success) {
    throw new Error(result.message || "Password change failed");
  }
}

export async function getUserSettings(): Promise<UserSettings> {
  const token = getToken();
  if (!token) throw new Error("Not signed in");

  const url = buildApiUrl("/api/user/settings");
  const response = await fetch(url, {
    headers: { Authorization: `Bearer ${token}` },
  });

  if (!response.ok) {
    // 返回默认设置
    return {
      theme: "system",
      language: "en",
      emailNotifications: true,
      pushNotifications: false,
    };
  }

  const result = (await response.json()) as ApiResponse<UserSettings>;
  return result.data || {
    theme: "system",
      language: "en",
    emailNotifications: true,
    pushNotifications: false,
  };
}

export async function updateUserSettings(settings: UserSettings): Promise<UserSettings> {
  const token = getToken();
  if (!token) throw new Error("Not signed in");

  const url = buildApiUrl("/api/user/settings");
  const response = await fetch(url, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify(settings),
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.message || "Failed to save settings");
  }

  const result = (await response.json()) as ApiResponse<UserSettings>;
  if (!result.success || !result.data) {
    throw new Error(result.message || "Failed to save settings");
  }

  return result.data;
}

export interface SystemVersion {
  version: string;
  assemblyVersion: string;
  productName: string;
}

export async function getSystemVersion(): Promise<SystemVersion> {
  const url = buildApiUrl("/api/system/version");
  const response = await fetch(url);

  if (!response.ok) {
    return {
      version: "1.0.0",
      assemblyVersion: "1.0.0.0",
      productName: "OpenDeepWiki",
    };
  }

  const result = (await response.json()) as ApiResponse<SystemVersion>;
  return result.data || {
    version: "1.0.0",
    assemblyVersion: "1.0.0.0",
    productName: "OpenDeepWiki",
  };
}
