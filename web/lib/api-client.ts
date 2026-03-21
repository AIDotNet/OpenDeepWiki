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

function normalizeApiErrorMessage(message: string): string {
  const normalized = message.replace(/\r/g, "").trim();
  if (!normalized) {
    return "请求失败";
  }

  const lines = normalized
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.length > 0);

  const firstMeaningfulLine =
    lines.find((line) => !line.startsWith("at ") && line !== "HEADERS" && !/^=+$/.test(line)) ||
    normalized;

  const exceptionMatch = firstMeaningfulLine.match(/^[A-Za-z0-9_.]+Exception:\s*(.+)$/);
  if (exceptionMatch?.[1]) {
    return exceptionMatch[1].trim();
  }

  return firstMeaningfulLine;
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
  const isFormData = typeof FormData !== "undefined" && body instanceof FormData;

  const headers: Record<string, string> = {
    ...(customHeaders as Record<string, string>),
  };

  if (!isFormData && body !== undefined && !headers["Content-Type"]) {
    headers["Content-Type"] = "application/json";
  }

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
    body:
      body === undefined
        ? undefined
        : isFormData
          ? (body as FormData)
          : JSON.stringify(body),
  });

  // 处理 401 未授权
  if (response.status === 401) {
    removeToken();
    throw new ApiError("请先登录", 401);
  }

  if (!response.ok) {
    let errorMessage = "请求失败";
    let errorData: unknown;

    try {
      const rawError = await response.text();
      if (rawError) {
        try {
          errorData = JSON.parse(rawError);
          if (
            typeof errorData === "object" &&
            errorData !== null
          ) {
            const message =
              "message" in errorData && typeof errorData.message === "string"
                ? errorData.message
                : "error" in errorData && typeof errorData.error === "string"
                  ? errorData.error
                  : null;

            errorMessage = normalizeApiErrorMessage(message || rawError);
          } else {
            errorMessage = normalizeApiErrorMessage(rawError);
          }
        } catch {
          errorMessage = normalizeApiErrorMessage(rawError);
          errorData = rawError;
        }
      }
    } catch {
      // Ignore body read failures and fall back to the default message.
    }
    throw new ApiError(errorMessage, response.status, errorData);
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
