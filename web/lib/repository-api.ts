import type { 
  RepoDocResponse, 
  RepoTreeResponse, 
  RepoBranchesResponse,
  GitBranchesResponse,
  RepositorySubmitRequest, 
  RepositoryListResponse,
  RepositoryItemResponse,
  UpdateVisibilityRequest,
  UpdateVisibilityResponse,
  ProcessingLogResponse,
  GitRepoCheckResponse,
  MindMapResponse
} from "@/types/repository";
import { api, buildApiUrl } from "./api-client";

function encodePathSegments(path: string) {
  return path
    .split("/")
    .map((segment) => encodeURIComponent(segment))
    .join("/");
}

export async function fetchRepoBranches(owner: string, repo: string) {
  const url = buildApiUrl(
    `/api/v1/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/branches`,
  );

  const response = await fetch(url, { cache: "no-store" });

  if (!response.ok) {
    throw new Error("Failed to fetch repository branches");
  }

  return (await response.json()) as RepoBranchesResponse;
}

/**
 * Fetch branches from Git platform API (GitHub/Gitee/GitLab)
 */
export async function fetchGitBranches(gitUrl: string): Promise<GitBranchesResponse> {
  const params = new URLSearchParams();
  params.set("gitUrl", gitUrl);
  
  const url = buildApiUrl(`/api/v1/repositories/branches?${params.toString()}`);

  const response = await fetch(url, { cache: "no-store" });

  if (!response.ok) {
    return { branches: [], defaultBranch: null, isSupported: false };
  }

  return (await response.json()) as GitBranchesResponse;
}

export async function fetchRepoTree(owner: string, repo: string, branch?: string, lang?: string) {
  const params = new URLSearchParams();
  if (branch) params.set("branch", branch);
  if (lang) params.set("lang", lang);
  
  const queryString = params.toString();
  const url = buildApiUrl(
    `/api/v1/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/tree${queryString ? `?${queryString}` : ""}`,
  );

  const response = await fetch(url, { cache: "no-store" });

  if (!response.ok) {
    throw new Error("Failed to fetch repository tree");
  }

  return (await response.json()) as RepoTreeResponse;
}

export async function fetchRepoDoc(owner: string, repo: string, slug: string, branch?: string, lang?: string) {
  const encodedSlug = encodePathSegments(slug);
  const params = new URLSearchParams();
  if (branch) params.set("branch", branch);
  if (lang) params.set("lang", lang);
  
  const queryString = params.toString();
  const url = buildApiUrl(
    `/api/v1/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/docs/${encodedSlug}${queryString ? `?${queryString}` : ""}`,
  );

  const response = await fetch(url, { cache: "no-store" });

  if (!response.ok) {
    throw new Error("Failed to fetch repository doc");
  }

  return (await response.json()) as RepoDocResponse;
}


/**
 * Submit a repository for wiki generation
 * 自动携带用户 token 进行认证
 */
export async function submitRepository(
  request: RepositorySubmitRequest
): Promise<RepositoryItemResponse> {
  return api.post<RepositoryItemResponse>("/api/v1/repositories/submit", request);
}

/**
 * Fetch repository list with optional filters
 */
export async function fetchRepositoryList(params?: {
  page?: number;
  pageSize?: number;
  ownerId?: string;
  status?: number;
  keyword?: string;
  language?: string;
  sortBy?: 'createdAt' | 'updatedAt' | 'status';
  sortOrder?: 'asc' | 'desc';
  isPublic?: boolean;
}): Promise<RepositoryListResponse> {
  const searchParams = new URLSearchParams();
  
  // page and pageSize are required by the backend API
  searchParams.set("page", (params?.page ?? 1).toString());
  searchParams.set("pageSize", (params?.pageSize ?? 20).toString());
  if (params?.ownerId) searchParams.set("ownerId", params.ownerId);
  if (params?.status !== undefined) searchParams.set("status", params.status.toString());
  if (params?.keyword) searchParams.set("keyword", params.keyword);
  if (params?.language) searchParams.set("language", params.language);
  if (params?.sortBy) searchParams.set("sortBy", params.sortBy);
  if (params?.sortOrder) searchParams.set("sortOrder", params.sortOrder);
  if (params?.isPublic !== undefined) searchParams.set("isPublic", params.isPublic.toString());

  const queryString = searchParams.toString();
  const url = buildApiUrl(`/api/v1/repositories/list${queryString ? `?${queryString}` : ""}`);

  const response = await fetch(url, { cache: "no-store" });

  if (!response.ok) {
    throw new Error("Failed to fetch repository list");
  }

  return await response.json();
}


/**
 * Update repository visibility (public/private)
 * 自动携带用户 token 进行认证
 */
export async function updateRepositoryVisibility(
  request: UpdateVisibilityRequest
): Promise<UpdateVisibilityResponse> {
  return api.post<UpdateVisibilityResponse>("/api/v1/repositories/visibility", request);
}

/**
 * Fetch repository status (client-side polling)
 */
export async function fetchRepoStatus(owner: string, repo: string): Promise<RepoTreeResponse> {
  const url = buildApiUrl(
    `/api/v1/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/tree`,
  );

  const response = await fetch(url, { cache: "no-store" });

  if (!response.ok) {
    throw new Error("Failed to fetch repository status");
  }

  return (await response.json()) as RepoTreeResponse;
}


/**
 * Fetch repository processing logs
 */
export async function fetchProcessingLogs(
  owner: string,
  repo: string,
  since?: Date,
  limit: number = 100
): Promise<ProcessingLogResponse> {
  const params = new URLSearchParams();
  if (since) {
    params.set("since", since.toISOString());
  }
  params.set("limit", limit.toString());

  const queryString = params.toString();
  const url = buildApiUrl(
    `/api/v1/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/processing-logs${queryString ? `?${queryString}` : ""}`
  );

  const response = await fetch(url, { cache: "no-store" });

  if (!response.ok) {
    throw new Error("Failed to fetch processing logs");
  }

  return (await response.json()) as ProcessingLogResponse;
}


/**
 * Check if a GitHub repository exists
 */
export async function checkGitHubRepo(
  owner: string,
  repo: string
): Promise<GitRepoCheckResponse> {
  const url = buildApiUrl(
    `/api/v1/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/check`
  );

  const response = await fetch(url, { cache: "no-store" });

  if (!response.ok) {
    return {
      exists: false,
      name: null,
      description: null,
      defaultBranch: null,
      starCount: 0,
      forkCount: 0,
      language: null,
      avatarUrl: null,
      isPrivate: false,
      gitUrl: null,
    };
  }

  return (await response.json()) as GitRepoCheckResponse;
}

/**
 * Regenerate repository documentation
 * 重新生成仓库文档
 */
export async function regenerateRepository(
  owner: string,
  repo: string
): Promise<{ success: boolean; errorMessage?: string }> {
  return api.post<{ success: boolean; errorMessage?: string }>(
    "/api/v1/repositories/regenerate",
    { owner, repo }
  );
}

/**
 * Fetch repository mind map
 * 获取仓库项目架构思维导图
 */
export async function fetchMindMap(
  owner: string,
  repo: string,
  branch?: string,
  lang?: string
): Promise<MindMapResponse> {
  const params = new URLSearchParams();
  if (branch) params.set("branch", branch);
  if (lang) params.set("lang", lang);

  const queryString = params.toString();
  const url = buildApiUrl(
    `/api/v1/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/mindmap${queryString ? `?${queryString}` : ""}`
  );

  const response = await fetch(url, { cache: "no-store" });

  if (!response.ok) {
    throw new Error("Failed to fetch mind map");
  }

  return (await response.json()) as MindMapResponse;
}
