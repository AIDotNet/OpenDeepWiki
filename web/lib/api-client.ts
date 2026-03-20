/**
 * 统一的 API 客户端模块
 * 自动处理 token 认证、错误处理等通用逻辑
 */

import { getApiProxyUrl } from "./env";
import { getToken, removeToken } from "./auth-api";
const API_BASE_URL = getApiProxyUrl();

function buildApiUrl(path: string): string {
  if (!API_BASE_URL) {
    return path;
  }
  const trimmedBase = API_BASE_URL.endsWith("/")
    ? API_BASE_URL.slice(0, -1)
    : API_BASE_URL;
  return `${trimmedBase}${path}`;
}

export interface ApiClientOptions extends Omit<RequestInit, "body"> {
  body?: unknown;
  /** 是否跳过自动添加 token，默认 false */
  skipAuth?: boolean;
}

export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
    public data?: unknown
  ) {
    super(message);
    this.name = "ApiError";
  }
}

/**
 * 统一的 API 请求方法
 * - 自动添加 Authorization header（如果有 token）
 * - 自动处理 JSON 序列化
 * - 统一错误处理
 */
export async function apiClient<T>(
  path: string,
  options: ApiClientOptions = {}
): Promise<T> {
  const { body, skipAuth = false, headers: customHeaders, ...restOptions } = options;

  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    ...(customHeaders as Record<string, string>),
  };

  // 自动添加 token
  if (!skipAuth) {
    const token = getToken();
    if (token) {
      headers["Authorization"] = `Bearer ${token}`;
    }
  }

  const url = buildApiUrl(path);

  const response = await fetch(url, {
    ...restOptions,
    headers,
    body: body ? JSON.stringify(body) : undefined,
  });

  // 处理 401 未授权
  if (response.status === 401) {
    removeToken();
    throw new ApiError("Please sign in first", 401);
  }

  if (!response.ok) {
    let errorMessage = "Request failed";
    try {
      const errorData = await response.json();
      errorMessage = errorData.message || errorData.error || errorMessage;
    } catch {
      errorMessage = await response.text() || errorMessage;
    }
    throw new ApiError(errorMessage, response.status);
  }

  // 处理空响应
  const contentType = response.headers.get("content-type");
  if (!contentType || !contentType.includes("application/json")) {
    return {} as T;
  }

  return response.json();
}

// 便捷方法
export const api = {
  get: <T>(path: string, options?: Omit<ApiClientOptions, "method">) =>
    apiClient<T>(path, { ...options, method: "GET" }),

  post: <T>(path: string, body?: unknown, options?: Omit<ApiClientOptions, "method" | "body">) =>
    apiClient<T>(path, { ...options, method: "POST", body }),

  put: <T>(path: string, body?: unknown, options?: Omit<ApiClientOptions, "method" | "body">) =>
    apiClient<T>(path, { ...options, method: "PUT", body }),

  patch: <T>(path: string, body?: unknown, options?: Omit<ApiClientOptions, "method" | "body">) =>
    apiClient<T>(path, { ...options, method: "PATCH", body }),

  delete: <T>(path: string, options?: Omit<ApiClientOptions, "method">) =>
    apiClient<T>(path, { ...options, method: "DELETE" }),
};

export { buildApiUrl };
