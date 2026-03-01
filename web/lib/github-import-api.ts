import { getToken } from "./auth-api";
import { getApiProxyUrl } from "./env";
import type { GitHubInstallation, GitHubRepo, BatchImportResult } from "./admin-api";

// Re-export shared types for convenience
export type { GitHubInstallation, GitHubRepo, BatchImportResult };

const API_BASE_URL = getApiProxyUrl();

function buildApiUrl(path: string) {
  if (!API_BASE_URL) {
    return path;
  }
  const trimmedBase = API_BASE_URL.endsWith("/") ? API_BASE_URL.slice(0, -1) : API_BASE_URL;
  return `${trimmedBase}${path}`;
}

async function fetchWithAuth(url: string, options: RequestInit = {}) {
  const token = getToken();
  const headers: HeadersInit = {
    "Content-Type": "application/json",
    ...(options.headers || {}),
  };

  if (token) {
    (headers as Record<string, string>)["Authorization"] = `Bearer ${token}`;
  }

  const response = await fetch(url, { ...options, headers });

  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.message || `Request failed: ${response.status}`);
  }

  return response.json();
}

export interface UserGitHubStatus {
  available: boolean;
  installations: GitHubInstallation[];
}

export interface GitHubRepoList {
  totalCount: number;
  repositories: GitHubRepo[];
  page: number;
  perPage: number;
}

/**
 * Get GitHub App status and connected installations (user-level)
 */
export async function getUserGitHubStatus(): Promise<UserGitHubStatus> {
  const url = buildApiUrl("/api/github/status");
  const result = await fetchWithAuth(url);
  return result.data;
}

/**
 * List repos from a GitHub installation (user-level)
 */
export async function getUserInstallationRepos(
  installationId: number,
  page: number = 1,
  perPage: number = 100
): Promise<GitHubRepoList> {
  const params = new URLSearchParams({
    page: page.toString(),
    perPage: perPage.toString(),
  });
  const url = buildApiUrl(`/api/github/installations/${installationId}/repos?${params}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

/**
 * Import repos for the current user (user-level)
 */
export async function userImportRepos(request: {
  installationId: number;
  departmentId?: string;
  languageCode: string;
  repos: {
    fullName: string;
    name: string;
    owner: string;
    cloneUrl: string;
    defaultBranch: string;
    private: boolean;
    language?: string;
    stargazersCount: number;
    forksCount: number;
  }[];
}): Promise<BatchImportResult> {
  const url = buildApiUrl("/api/github/import");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(request),
  });
  return result.data;
}
