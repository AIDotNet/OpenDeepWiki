import { describe, expect, it, vi, beforeEach } from "vitest";

vi.mock("@/lib/env", () => ({
  getApiProxyUrl: () => "",
}));

vi.mock("@/lib/auth-api", () => ({
  getToken: () => null,
  removeToken: vi.fn(),
}));

import { ApiError, apiClient } from "@/lib/api-client";

describe("apiClient", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("在非 JSON 错误响应时只读取一次 body 并提取首条有效错误消息", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response("System.InvalidOperationException: 当前未配置允许导入的本地目录根路径\r\n   at OpenDeepWiki.Services.Repositories.RepositoryService.EnsureLocalPathAllowed(String fullPath)\r\nHEADERS\r\n=======\r\nHost: localhost:5265", {
        status: 400,
        headers: {
          "Content-Type": "text/plain; charset=utf-8",
        },
      })
    );

    await expect(apiClient("/api/v1/repositories/submit-local", { method: "POST" })).rejects.toMatchObject({
      message: "当前未配置允许导入的本地目录根路径",
      status: 400,
    } satisfies Partial<ApiError>);
  });

  it("在 JSON 错误响应时优先提取并清洗 message 字段", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify({ message: "System.InvalidOperationException: 本地目录不在允许导入的白名单范围内\r\n   at Service.Handle()" }), {
        status: 400,
        headers: {
          "Content-Type": "application/json; charset=utf-8",
        },
      })
    );

    await expect(apiClient("/api/v1/repositories/submit-local", { method: "POST" })).rejects.toMatchObject({
      message: "本地目录不在允许导入的白名单范围内",
      status: 400,
      data: { message: "System.InvalidOperationException: 本地目录不在允许导入的白名单范围内\r\n   at Service.Handle()" },
    } satisfies Partial<ApiError>);
  });
});
