import type { RepoTreeNode } from "@/types/repository";
import type { AdminIncrementalTask } from "@/lib/admin-api";
import { getIncrementalUpdateTask } from "@/lib/admin-api";

export interface DocOption {
  title: string;
  slug: string;
}

export function flattenDocNodes(nodes: RepoTreeNode[]): DocOption[] {
  const docs: DocOption[] = [];
  const walk = (list: RepoTreeNode[]) => {
    list.forEach((node) => {
      if (node.children && node.children.length > 0) {
        walk(node.children);
        return;
      }
      docs.push({ title: node.title, slug: node.slug });
    });
  };
  walk(nodes);
  return docs;
}

export function findNodeTrail(nodes: RepoTreeNode[], targetSlug: string, trail: string[] = []): string[] | null {
  for (const node of nodes) {
    const nextTrail = [...trail, node.slug];
    if (node.slug === targetSlug) {
      return nextTrail;
    }
    if (node.children?.length) {
      const result = findNodeTrail(node.children, targetSlug, nextTrail);
      if (result) {
        return result;
      }
    }
  }
  return null;
}

export function statusBadgeClass(status: string) {
  const value = status.toLowerCase();
  if (value === "completed" || value === "已完成") return "bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200";
  if (value === "processing" || value === "处理中") return "bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-200";
  if (value === "pending" || value === "待处理") return "bg-gray-100 text-gray-800 dark:bg-gray-800 dark:text-gray-200";
  if (value === "failed" || value === "失败") return "bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-200";
  if (value === "cancelled" || value === "已取消") return "bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-200";
  return "bg-muted text-muted-foreground";
}

export function mapTaskStatusToAdminTask(
  source: Awaited<ReturnType<typeof getIncrementalUpdateTask>>
): AdminIncrementalTask {
  return {
    taskId: source.taskId,
    branchId: source.branchId,
    branchName: source.branchName,
    status: source.status,
    priority: source.priority,
    isManualTrigger: source.isManualTrigger,
    retryCount: source.retryCount,
    previousCommitId: source.previousCommitId,
    targetCommitId: source.targetCommitId,
    errorMessage: source.errorMessage,
    createdAt: source.createdAt,
    startedAt: source.startedAt,
    completedAt: source.completedAt,
  };
}

export function normalizeTaskStatus(status: string) {
  const value = status.toLowerCase();
  if (value.includes("completed") || value.includes("完成")) return "completed";
  if (value.includes("processing") || value.includes("处理")) return "processing";
  if (value.includes("pending") || value.includes("待")) return "pending";
  if (value.includes("failed") || value.includes("失败")) return "failed";
  if (value.includes("cancel") || value.includes("取消")) return "cancelled";
  return "other";
}
