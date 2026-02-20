import { getApiProxyUrl } from "./env";
import { getToken } from "./auth-api";

function buildApiUrl(path: string) {
  const baseUrl = getApiProxyUrl();
  if (!baseUrl) {
    return path;
  }
  const trimmedBase = baseUrl.endsWith("/") ? baseUrl.slice(0, -1) : baseUrl;
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

// ==================== Statistics API ====================

export interface DailyRepositoryStatistic {
  date: string;
  processedCount: number;
  submittedCount: number;
}

export interface DailyUserStatistic {
  date: string;
  newUserCount: number;
}

export interface DashboardStatistics {
  repositoryStats: DailyRepositoryStatistic[];
  userStats: DailyUserStatistic[];
}

export interface DailyTokenUsage {
  date: string;
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
}

export interface TokenUsageStatistics {
  dailyUsages: DailyTokenUsage[];
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
}

export async function getDashboardStatistics(days: number = 7): Promise<DashboardStatistics> {
  const url = buildApiUrl(`/api/admin/statistics/dashboard?days=${days}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getTokenUsageStatistics(days: number = 7): Promise<TokenUsageStatistics> {
  const url = buildApiUrl(`/api/admin/statistics/token-usage?days=${days}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

// ==================== Repository API ====================

export interface AdminRepository {
  id: string;
  gitUrl: string;
  repoName: string;
  orgName: string;
  isPublic: boolean;
  status: number;
  statusText: string;
  starCount: number;
  forkCount: number;
  bookmarkCount: number;
  viewCount: number;
  ownerUserId?: string;
  ownerUserName?: string;
  createdAt: string;
  updatedAt?: string;
}

export interface RepositoryListResponse {
  items: AdminRepository[];
  total: number;
  page: number;
  pageSize: number;
}

export async function getRepositories(
  page: number = 1,
  pageSize: number = 20,
  search?: string,
  status?: number
): Promise<RepositoryListResponse> {
  const params = new URLSearchParams({
    page: page.toString(),
    pageSize: pageSize.toString(),
  });
  if (search) params.append("search", search);
  if (status !== undefined) params.append("status", status.toString());

  const url = buildApiUrl(`/api/admin/repositories?${params}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getRepository(id: string): Promise<AdminRepository> {
  const url = buildApiUrl(`/api/admin/repositories/${id}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function updateRepository(id: string, data: {
  isPublic?: boolean;
  authAccount?: string;
  authPassword?: string;
}): Promise<void> {
  const url = buildApiUrl(`/api/admin/repositories/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteRepository(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/repositories/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

export async function updateRepositoryStatus(id: string, status: number): Promise<void> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/status`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify({ status }),
  });
}

// 同步单个仓库统计信息
export interface SyncStatsResult {
  success: boolean;
  message?: string;
  starCount: number;
  forkCount: number;
}

export async function syncRepositoryStats(id: string): Promise<SyncStatsResult> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/sync-stats`);
  const result = await fetchWithAuth(url, { method: "POST" });
  return result.data;
}

// 批量同步统计信息
export interface BatchSyncItemResult {
  id: string;
  repoName: string;
  success: boolean;
  message?: string;
  starCount: number;
  forkCount: number;
}

export interface BatchSyncStatsResult {
  totalCount: number;
  successCount: number;
  failedCount: number;
  results: BatchSyncItemResult[];
}

export async function batchSyncRepositoryStats(ids: string[]): Promise<BatchSyncStatsResult> {
  const url = buildApiUrl("/api/admin/repositories/batch/sync-stats");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify({ ids }),
  });
  return result.data;
}

// 批量删除仓库
export interface BatchDeleteResult {
  totalCount: number;
  successCount: number;
  failedCount: number;
  failedIds: string[];
}

export async function batchDeleteRepositories(ids: string[]): Promise<BatchDeleteResult> {
  const url = buildApiUrl("/api/admin/repositories/batch/delete");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify({ ids }),
  });
  return result.data;
}

export interface AdminBranchLanguage {
  id: string;
  languageCode: string;
  isDefault: boolean;
  catalogCount: number;
  documentCount: number;
  createdAt: string;
}

export interface AdminRepositoryBranch {
  id: string;
  name: string;
  lastCommitId?: string;
  lastProcessedAt?: string;
  languages: AdminBranchLanguage[];
}

export interface AdminIncrementalTask {
  taskId: string;
  branchId: string;
  branchName?: string;
  status: string;
  priority: number;
  isManualTrigger: boolean;
  retryCount: number;
  previousCommitId?: string;
  targetCommitId?: string;
  errorMessage?: string;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
}

export interface AdminRepositoryManagement {
  repositoryId: string;
  orgName: string;
  repoName: string;
  status: number;
  statusText: string;
  branches: AdminRepositoryBranch[];
  recentIncrementalTasks: AdminIncrementalTask[];
}

export interface AdminRepositoryOperationResult {
  success: boolean;
  message: string;
}

export interface RegenerateRepositoryDocumentPayload {
  branchId: string;
  languageCode: string;
  documentPath: string;
}

export interface UpdateRepositoryDocumentContentPayload {
  branchId: string;
  languageCode: string;
  documentPath: string;
  content: string;
}

export async function getRepositoryManagement(id: string): Promise<AdminRepositoryManagement> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/management`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function regenerateRepository(id: string): Promise<AdminRepositoryOperationResult> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/regenerate`);
  const result = await fetchWithAuth(url, { method: "POST" });
  return result.data;
}

export async function regenerateRepositoryDocument(
  id: string,
  data: RegenerateRepositoryDocumentPayload
): Promise<AdminRepositoryOperationResult> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/documents/regenerate`);
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function updateRepositoryDocumentContent(
  id: string,
  data: UpdateRepositoryDocumentContentPayload
): Promise<AdminRepositoryOperationResult> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/documents/content`);
  const result = await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
  return result.data;
}

export interface IncrementalUpdateTriggerResult {
  success: boolean;
  taskId: string;
  status: string;
  message: string;
}

export interface IncrementalUpdateTaskStatus {
  success: boolean;
  taskId: string;
  repositoryId: string;
  repositoryName?: string;
  branchId: string;
  branchName?: string;
  status: string;
  priority: number;
  isManualTrigger: boolean;
  previousCommitId?: string;
  targetCommitId?: string;
  retryCount: number;
  errorMessage?: string;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
}

export interface IncrementalUpdateRetryResult {
  success: boolean;
  taskId: string;
  status: string;
  retryCount: number;
  message: string;
}

export async function triggerRepositoryIncrementalUpdate(
  repositoryId: string,
  branchId: string
): Promise<IncrementalUpdateTriggerResult> {
  const url = buildApiUrl(`/api/v1/repositories/${repositoryId}/branches/${branchId}/incremental-update`);
  return fetchWithAuth(url, { method: "POST" });
}

export async function getIncrementalUpdateTask(
  taskId: string
): Promise<IncrementalUpdateTaskStatus> {
  const url = buildApiUrl(`/api/v1/incremental-updates/${taskId}`);
  return fetchWithAuth(url);
}

export async function retryIncrementalUpdateTask(
  taskId: string
): Promise<IncrementalUpdateRetryResult> {
  const url = buildApiUrl(`/api/v1/incremental-updates/${taskId}/retry`);
  return fetchWithAuth(url, { method: "POST" });
}

// ==================== User API ====================

export interface AdminUser {
  id: string;
  name: string;
  email?: string;
  avatar?: string;
  status: number;
  roles: string[];
  createdAt: string;
  updatedAt?: string;
}

export interface UserListResponse {
  items: AdminUser[];
  total: number;
  page: number;
  pageSize: number;
}

export async function getUsers(
  page: number = 1,
  pageSize: number = 20,
  search?: string,
  roleId?: string
): Promise<UserListResponse> {
  const params = new URLSearchParams({
    page: page.toString(),
    pageSize: pageSize.toString(),
  });
  if (search) params.append("search", search);
  if (roleId) params.append("roleId", roleId);

  const url = buildApiUrl(`/api/admin/users?${params}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getUser(id: string): Promise<AdminUser> {
  const url = buildApiUrl(`/api/admin/users/${id}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function createUser(data: {
  name: string;
  email: string;
  password: string;
  roleIds?: string[];
}): Promise<AdminUser> {
  const url = buildApiUrl("/api/admin/users");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function updateUser(id: string, data: {
  name?: string;
  email?: string;
  avatar?: string;
}): Promise<void> {
  const url = buildApiUrl(`/api/admin/users/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteUser(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/users/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

export async function updateUserStatus(id: string, status: number): Promise<void> {
  const url = buildApiUrl(`/api/admin/users/${id}/status`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify({ status }),
  });
}

export async function updateUserRoles(id: string, roleIds: string[]): Promise<void> {
  const url = buildApiUrl(`/api/admin/users/${id}/roles`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify({ roleIds }),
  });
}

export async function resetUserPassword(id: string, newPassword: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/users/${id}/reset-password`);
  await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify({ newPassword }),
  });
}

// ==================== Role API ====================

export interface AdminRole {
  id: string;
  name: string;
  description?: string;
  isSystem: boolean;
  userCount: number;
  createdAt: string;
}

export async function getRoles(): Promise<AdminRole[]> {
  const url = buildApiUrl("/api/admin/roles");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getRole(id: string): Promise<AdminRole> {
  const url = buildApiUrl(`/api/admin/roles/${id}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function createRole(data: {
  name: string;
  description?: string;
}): Promise<AdminRole> {
  const url = buildApiUrl("/api/admin/roles");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function updateRole(id: string, data: {
  name?: string;
  description?: string;
}): Promise<void> {
  const url = buildApiUrl(`/api/admin/roles/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteRole(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/roles/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

// ==================== Tools API - MCP ====================

export interface McpConfig {
  id: string;
  name: string;
  description?: string;
  serverUrl: string;
  apiKey?: string;
  isActive: boolean;
  sortOrder: number;
  createdAt: string;
}

export async function getMcpConfigs(): Promise<McpConfig[]> {
  const url = buildApiUrl("/api/admin/tools/mcps");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function createMcpConfig(data: {
  name: string;
  description?: string;
  serverUrl: string;
  apiKey?: string;
  isActive?: boolean;
  sortOrder?: number;
}): Promise<McpConfig> {
  const url = buildApiUrl("/api/admin/tools/mcps");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function updateMcpConfig(id: string, data: {
  name?: string;
  description?: string;
  serverUrl?: string;
  apiKey?: string;
  isActive?: boolean;
  sortOrder?: number;
}): Promise<void> {
  const url = buildApiUrl(`/api/admin/tools/mcps/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteMcpConfig(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/tools/mcps/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

// ==================== Tools API - Skill (Agent Skills 标准) ====================

export interface SkillFileInfo {
  fileName: string;
  relativePath: string;
  size: number;
  lastModified: string;
}

export interface SkillConfig {
  id: string;
  name: string;
  description: string;
  license?: string;
  compatibility?: string;
  allowedTools?: string;
  folderPath: string;
  isActive: boolean;
  sortOrder: number;
  author?: string;
  version: string;
  source: string;
  sourceUrl?: string;
  hasScripts: boolean;
  hasReferences: boolean;
  hasAssets: boolean;
  skillMdSize: number;
  totalSize: number;
  createdAt: string;
}

export interface SkillDetail extends SkillConfig {
  skillMdContent: string;
  scripts: SkillFileInfo[];
  references: SkillFileInfo[];
  assets: SkillFileInfo[];
}

export async function getSkillConfigs(): Promise<SkillConfig[]> {
  const url = buildApiUrl("/api/admin/tools/skills");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getSkillDetail(id: string): Promise<SkillDetail> {
  const url = buildApiUrl(`/api/admin/tools/skills/${id}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function uploadSkill(file: File): Promise<SkillConfig> {
  const token = getToken();
  const formData = new FormData();
  formData.append("file", file);

  const url = buildApiUrl("/api/admin/tools/skills/upload");
  const headers: HeadersInit = {};
  if (token) {
    headers["Authorization"] = `Bearer ${token}`;
  }

  const response = await fetch(url, {
    method: "POST",
    headers,
    body: formData,
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.message || `上传失败: ${response.status}`);
  }

  const result = await response.json();
  return result.data;
}

export async function updateSkillConfig(id: string, data: {
  isActive?: boolean;
  sortOrder?: number;
}): Promise<void> {
  const url = buildApiUrl(`/api/admin/tools/skills/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteSkillConfig(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/tools/skills/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

export async function getSkillFileContent(id: string, filePath: string): Promise<string> {
  const url = buildApiUrl(`/api/admin/tools/skills/${id}/files/${filePath}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function refreshSkills(): Promise<void> {
  const url = buildApiUrl("/api/admin/tools/skills/refresh");
  await fetchWithAuth(url, { method: "POST" });
}

// ==================== Tools API - Model ====================

export interface ModelConfig {
  id: string;
  name: string;
  provider: string;
  modelId: string;
  endpoint?: string;
  apiKey?: string;
  isDefault: boolean;
  isActive: boolean;
  description?: string;
  createdAt: string;
}

export async function getModelConfigs(): Promise<ModelConfig[]> {
  const url = buildApiUrl("/api/admin/tools/models");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function createModelConfig(data: {
  name: string;
  provider: string;
  modelId: string;
  endpoint?: string;
  apiKey?: string;
  isDefault?: boolean;
  isActive?: boolean;
  description?: string;
}): Promise<ModelConfig> {
  const url = buildApiUrl("/api/admin/tools/models");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function updateModelConfig(id: string, data: {
  name?: string;
  provider?: string;
  modelId?: string;
  endpoint?: string;
  apiKey?: string;
  isDefault?: boolean;
  isActive?: boolean;
  description?: string;
}): Promise<void> {
  const url = buildApiUrl(`/api/admin/tools/models/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteModelConfig(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/tools/models/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

// ==================== Chat Provider API ====================

export interface ChatProviderStatus {
  platform: string;
  displayName: string;
  isEnabled: boolean;
  isRegistered: boolean;
  webhookUrl?: string;
  messageInterval: number;
  maxRetryCount: number;
}

export interface ChatProviderConfig {
  platform: string;
  displayName: string;
  isEnabled: boolean;
  configData: string;
  webhookUrl?: string;
  messageInterval: number;
  maxRetryCount: number;
}

export async function getChatProviderConfigs(): Promise<ChatProviderStatus[]> {
  const url = buildApiUrl("/api/chat/admin/providers");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getChatProviderConfig(platform: string): Promise<ChatProviderConfig> {
  const url = buildApiUrl(`/api/chat/admin/providers/${platform}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function saveChatProviderConfig(data: ChatProviderConfig): Promise<void> {
  const url = buildApiUrl("/api/chat/admin/providers");
  await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
}

export async function deleteChatProviderConfig(platform: string): Promise<void> {
  const url = buildApiUrl(`/api/chat/admin/providers/${platform}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

export async function enableChatProvider(platform: string): Promise<void> {
  const url = buildApiUrl(`/api/chat/admin/providers/${platform}/enable`);
  await fetchWithAuth(url, { method: "POST" });
}

export async function disableChatProvider(platform: string): Promise<void> {
  const url = buildApiUrl(`/api/chat/admin/providers/${platform}/disable`);
  await fetchWithAuth(url, { method: "POST" });
}

export async function reloadChatProviderConfig(platform: string): Promise<void> {
  const url = buildApiUrl(`/api/chat/admin/providers/${platform}/reload`);
  await fetchWithAuth(url, { method: "POST" });
}

// ==================== Settings API ====================

export interface SystemSetting {
  id: string;
  key: string;
  value?: string;
  description?: string;
  category: string;
}

export async function getSettings(category?: string): Promise<SystemSetting[]> {
  const params = category ? `?category=${category}` : "";
  const url = buildApiUrl(`/api/admin/settings${params}`);
  const result = await fetchWithAuth(url);
  return result.data || [];
}

export async function getSetting(key: string): Promise<SystemSetting> {
  const url = buildApiUrl(`/api/admin/settings/${key}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function updateSettings(settings: { key: string; value: string }[]): Promise<void> {
  const url = buildApiUrl("/api/admin/settings");
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(settings),
  });
}


// ==================== Department API ====================

export interface AdminDepartment {
  id: string;
  name: string;
  parentId?: string;
  parentName?: string;
  description?: string;
  sortOrder: number;
  isActive: boolean;
  createdAt: string;
  children?: AdminDepartment[];
}

export async function getDepartments(): Promise<AdminDepartment[]> {
  const url = buildApiUrl("/api/admin/departments");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getDepartmentTree(): Promise<AdminDepartment[]> {
  const url = buildApiUrl("/api/admin/departments/tree");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getDepartment(id: string): Promise<AdminDepartment> {
  const url = buildApiUrl(`/api/admin/departments/${id}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function createDepartment(data: {
  name: string;
  parentId?: string;
  description?: string;
  sortOrder?: number;
  isActive?: boolean;
}): Promise<AdminDepartment> {
  const url = buildApiUrl("/api/admin/departments");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function updateDepartment(id: string, data: {
  name?: string;
  parentId?: string;
  description?: string;
  sortOrder?: number;
  isActive?: boolean;
}): Promise<void> {
  const url = buildApiUrl(`/api/admin/departments/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteDepartment(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/departments/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}


// ==================== Department Users & Repositories API ====================

export interface DepartmentUser {
  id: string;
  userId: string;
  userName: string;
  email?: string;
  avatar?: string;
  isManager: boolean;
  createdAt: string;
}

export interface DepartmentRepository {
  id: string;
  repositoryId: string;
  repoName: string;
  orgName: string;
  gitUrl?: string;
  status: number;
  assigneeUserName?: string;
  createdAt: string;
}

export async function getDepartmentUsers(departmentId: string): Promise<DepartmentUser[]> {
  const url = buildApiUrl(`/api/admin/departments/${departmentId}/users`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function addUserToDepartment(departmentId: string, userId: string, isManager: boolean = false): Promise<void> {
  const url = buildApiUrl(`/api/admin/departments/${departmentId}/users`);
  await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify({ userId, isManager }),
  });
}

export async function removeUserFromDepartment(departmentId: string, userId: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/departments/${departmentId}/users/${userId}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

export async function getDepartmentRepositories(departmentId: string): Promise<DepartmentRepository[]> {
  const url = buildApiUrl(`/api/admin/departments/${departmentId}/repositories`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function assignRepositoryToDepartment(
  departmentId: string,
  repositoryId: string,
  assigneeUserId: string
): Promise<void> {
  const url = buildApiUrl(`/api/admin/departments/${departmentId}/repositories`);
  await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify({ repositoryId, assigneeUserId }),
  });
}

export async function removeRepositoryFromDepartment(departmentId: string, repositoryId: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/departments/${departmentId}/repositories/${repositoryId}`);
  await fetchWithAuth(url, { method: "DELETE" });
}


// ==================== Chat Assistant Config API ====================

export interface SelectableItem {
  id: string;
  name: string;
  description?: string;
  isActive: boolean;
  isSelected: boolean;
}

export interface ChatAssistantConfig {
  id: string;
  isEnabled: boolean;
  enabledModelIds: string[];
  enabledMcpIds: string[];
  enabledSkillIds: string[];
  defaultModelId?: string;
  createdAt: string;
  updatedAt?: string;
}

export interface ChatAssistantConfigOptions {
  config: ChatAssistantConfig;
  availableModels: SelectableItem[];
  availableMcps: SelectableItem[];
  availableSkills: SelectableItem[];
}

export interface UpdateChatAssistantConfigRequest {
  isEnabled: boolean;
  enabledModelIds: string[];
  enabledMcpIds: string[];
  enabledSkillIds: string[];
  defaultModelId?: string;
}

export async function getChatAssistantConfig(): Promise<ChatAssistantConfigOptions> {
  const url = buildApiUrl("/api/admin/chat-assistant/config");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function updateChatAssistantConfig(
  data: UpdateChatAssistantConfigRequest
): Promise<ChatAssistantConfig> {
  const url = buildApiUrl("/api/admin/chat-assistant/config");
  const result = await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
  return result.data;
}

// ==================== GitHub Import API ====================

export interface GitHubInstallation {
  id: string;
  installationId: number;
  accountLogin: string;
  accountType: string;
  accountId: number;
  avatarUrl?: string;
  departmentId?: string;
  departmentName?: string;
  createdAt: string;
}

export interface GitHubStatus {
  configured: boolean;
  appName?: string;
  installations: GitHubInstallation[];
}

export interface GitHubRepo {
  id: number;
  fullName: string;
  name: string;
  owner: string;
  private: boolean;
  description?: string;
  language?: string;
  stargazersCount: number;
  forksCount: number;
  defaultBranch: string;
  cloneUrl: string;
  htmlUrl: string;
  alreadyImported: boolean;
}

export interface GitHubRepoList {
  totalCount: number;
  repositories: GitHubRepo[];
  page: number;
  perPage: number;
}

export interface BatchImportResult {
  totalRequested: number;
  imported: number;
  skipped: number;
  skippedRepos: string[];
  importedRepos: string[];
}

export async function getGitHubStatus(): Promise<GitHubStatus> {
  const url = buildApiUrl("/api/admin/github/status");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getGitHubInstallUrl(): Promise<{ url: string; appName: string }> {
  const url = buildApiUrl("/api/admin/github/install-url");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function storeGitHubInstallation(installationId: number): Promise<GitHubInstallation> {
  const url = buildApiUrl("/api/admin/github/installations");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify({ installationId }),
  });
  return result.data;
}

export async function getInstallationRepos(
  installationId: number,
  page: number = 1,
  perPage: number = 30
): Promise<GitHubRepoList> {
  const params = new URLSearchParams({
    page: page.toString(),
    perPage: perPage.toString(),
  });
  const url = buildApiUrl(`/api/admin/github/installations/${installationId}/repos?${params}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function batchImportRepos(request: {
  installationId: number;
  departmentId: string;
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
  const url = buildApiUrl("/api/admin/github/batch-import");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(request),
  });
  return result.data;
}
