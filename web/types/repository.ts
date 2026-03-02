export interface RepoTreeNode {
  title: string;
  slug: string;
  children: RepoTreeNode[];
}

export interface RepoTreeResponse {
  owner: string;
  repo: string;
  defaultSlug: string;
  nodes: RepoTreeNode[];
  status: number;
  statusName: RepositoryStatus;
  exists: boolean;
  currentBranch: string;
  currentLanguage: string;
}

export interface RepoBranchesResponse {
  branches: BranchItem[];
  languages: string[];
  defaultBranch: string;
  defaultLanguage: string;
}

export interface BranchItem {
  name: string;
  languages: string[];
}

// Git platform branches response (from GitHub/Gitee/GitLab API)
export interface GitBranchesResponse {
  branches: GitBranchItem[];
  defaultBranch: string | null;
  isSupported: boolean;
}

export interface GitBranchItem {
  name: string;
  isDefault: boolean;
}

export interface RepoDocResponse {
  exists: boolean;
  slug: string;
  content: string;
  sourceFiles: string[];
}

export interface RepoHeading {
  id: string;
  text: string;
  level: number;
}

// Repository submission and list types
export type RepositoryStatus = "Pending" | "Processing" | "Completed" | "Failed";

export interface RepositorySubmitRequest {
  gitUrl: string;
  repoName: string;
  orgName: string;
  authAccount?: string;
  authPassword?: string;
  branchName: string;
  languageCode: string;
  isPublic: boolean;
}

export interface RepositoryItemResponse {
  id: string;
  orgName: string;
  repoName: string;
  gitUrl: string;
  status: number;
  statusName: RepositoryStatus;
  isPublic: boolean;
  hasPassword: boolean;  // 新增：是否设置了密码，用于判断是否可设为私有
  createdAt: string;
  updatedAt?: string;
  starCount?: number;
  forkCount?: number;
  primaryLanguage?: string;
}

export interface RepositoryListResponse {
  items: RepositoryItemResponse[];
  total: number;
}

// Visibility update types for private repository management
export interface UpdateVisibilityRequest {
  repositoryId: string;
  isPublic: boolean;
}

export interface UpdateVisibilityResponse {
  id: string;
  isPublic: boolean;
  success: boolean;
  errorMessage?: string;
}

// Processing log types
export type ProcessingStep = "Workspace" | "Catalog" | "Content" | "Complete";

// 思维导图状态
export type MindMapStatus = "Pending" | "Processing" | "Completed" | "Failed";

// 思维导图状态数字到字符串的映射
export const MindMapStatusMap: Record<number, MindMapStatus> = {
  0: "Pending",
  1: "Processing",
  2: "Completed",
  3: "Failed",
};

// 思维导图响应
export interface MindMapResponse {
  owner: string;
  repo: string;
  branch: string;
  language: string;
  status: number;
  statusName: MindMapStatus;
  content: string | null;
}

// 思维导图节点（解析后的结构）
export interface MindMapNode {
  title: string;
  filePath?: string;
  level: number;
  children: MindMapNode[];
}

// 步骤数字到字符串的映射
export const ProcessingStepMap: Record<number, ProcessingStep> = {
  0: "Workspace",
  1: "Catalog",
  2: "Content",
  3: "Complete",
};

export interface ProcessingLogItem {
  id: string;
  step: number;
  stepName: ProcessingStep;
  message: string;
  isAiOutput: boolean;
  toolName?: string;
  createdAt: string;
}

export interface ProcessingLogResponse {
  status: number;
  statusName: RepositoryStatus;
  currentStep: number;
  currentStepName: ProcessingStep;
  totalDocuments: number;
  completedDocuments: number;
  startedAt: string | null;
  logs: ProcessingLogItem[];
}

// GitHub repo check response
export interface GitRepoCheckResponse {
  exists: boolean;
  name: string | null;
  description: string | null;
  defaultBranch: string | null;
  starCount: number;
  forkCount: number;
  language: string | null;
  avatarUrl: string | null;
  isPrivate: boolean;
  gitUrl: string | null;
}
