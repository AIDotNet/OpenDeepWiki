/**
 * SSR 进程内只读缓存层。
 *
 * 背景：爬虫（Googlebot 等）并发抓取页面时，每个 SSR 页面会向后端 API
 * 发起多次请求。此前所有 fetch 均为 no-store，后端（默认 SQLite）在
 * 高并发下锁库导致雪崩。本模块为匿名的 GET 请求提供：
 *
 * 1. TTL 缓存：相同 URL 在 TTL 内直接复用内存结果；
 * 2. 单飞去重（inflight dedup）：同一 URL 的并发请求只发出一个真实请求，
 *    防止缓存失效瞬间的击穿（dog-pile）；
 * 3. 负缓存：失败响应（404/5xx）用短 TTL 缓存，防止爬虫扫描不存在路径
 *    造成缓存穿透；
 * 4. 带 Authorization 的请求永不缓存，登录用户的私有数据不受影响。
 */

type CachePayload = {
  ok: boolean;
  status: number;
  data?: unknown;
};

type CacheEntry = {
  payload: CachePayload;
  expiresAt: number;
};

const MAX_ENTRIES = 500;
const store = new Map<string, CacheEntry>();
const inflight = new Map<string, Promise<CachePayload>>();

export const DEFAULT_SSR_CACHE_TTL_MS = 120_000;
export const SSR_ERROR_CACHE_TTL_MS = 60_000;

function hasAuthorizationHeader(init?: RequestInit): boolean {
  const headers = init?.headers;
  if (!headers) return false;
  if (headers instanceof Headers) {
    return headers.has("Authorization");
  }
  if (Array.isArray(headers)) {
    return headers.some(([key]) => key.toLowerCase() === "authorization");
  }
  return Object.keys(headers).some((key) => key.toLowerCase() === "authorization");
}

function evictIfNeeded() {
  if (store.size < MAX_ENTRIES) {
    return;
  }

  const now = Date.now();
  for (const [key, entry] of store) {
    if (entry.expiresAt <= now) {
      store.delete(key);
    }
  }

  // 仍然超限则按插入顺序淘汰最旧条目
  while (store.size >= MAX_ENTRIES) {
    const oldestKey = store.keys().next().value;
    if (oldestKey === undefined) {
      break;
    }
    store.delete(oldestKey);
  }
}

function unwrap<T>(payload: CachePayload): T {
  if (!payload.ok) {
    throw new Error(`Request failed with status ${payload.status}`);
  }
  return payload.data as T;
}

/**
 * 带进程内缓存与单飞去重的 GET JSON 请求。
 * - 匿名请求：读缓存，miss 时发起真实请求并回填；
 * - 带 Authorization 的请求：绕过缓存直连。
 */
export async function cachedFetchJson<T>(
  url: string,
  init?: RequestInit,
  ttlMs: number = DEFAULT_SSR_CACHE_TTL_MS,
  errorTtlMs: number = SSR_ERROR_CACHE_TTL_MS,
): Promise<T> {
  if (hasAuthorizationHeader(init)) {
    const response = await fetch(url, init);
    if (!response.ok) {
      throw new Error(`Request failed with status ${response.status}`);
    }
    return (await response.json()) as T;
  }

  const cached = store.get(url);
  if (cached && cached.expiresAt > Date.now()) {
    return unwrap<T>(cached.payload);
  }

  const pending = inflight.get(url);
  if (pending) {
    return unwrap<T>(await pending);
  }

  const promise = (async (): Promise<CachePayload> => {
    const response = await fetch(url, init);
    if (!response.ok) {
      return { ok: false, status: response.status };
    }

    const contentType = response.headers.get("content-type") ?? "";
    const data = contentType.includes("application/json")
      ? ((await response.json()) as unknown)
      : await response.text();
    return { ok: true, status: response.status, data };
  })()
    .then((payload) => {
      evictIfNeeded();
      store.set(url, {
        payload,
        expiresAt: Date.now() + (payload.ok ? ttlMs : errorTtlMs),
      });
      return payload;
    })
    .finally(() => {
      inflight.delete(url);
    });

  inflight.set(url, promise);
  return unwrap<T>(await promise);
}

/** 仅测试使用：清空缓存状态 */
export function clearSsrCacheForTests() {
  store.clear();
  inflight.clear();
}
