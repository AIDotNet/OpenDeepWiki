"use client";

import React, { useCallback, useEffect, useMemo, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Badge } from "@/components/ui/badge";
import {
  ArrowLeft,
  CheckCircle2,
  ChevronDown,
  ChevronRight,
  Clock3,
  Loader2,
  RefreshCw,
  RotateCcw,
  FileText,
  GitBranch,
  Languages,
  Zap,
  History,
  AlertTriangle,
  Sparkles,
  Pencil,
  Save,
  X,
} from "lucide-react";
import { toast } from "sonner";
import { useTranslations } from "@/hooks/use-translations";
import { useLocale } from "next-intl";
import {
  AdminIncrementalTask,
  AdminRepository,
  AdminRepositoryManagement,
  getIncrementalUpdateTask,
  getRepository,
  getRepositoryManagement,
  regenerateRepository,
  regenerateRepositoryDocument,
  retryIncrementalUpdateTask,
  syncRepositoryStats,
  triggerRepositoryIncrementalUpdate,
  updateRepositoryDocumentContent,
} from "@/lib/admin-api";
import { fetchProcessingLogs, fetchRepoDoc, fetchRepoTree } from "@/lib/repository-api";
import type { ProcessingLogResponse, RepoDocResponse, RepoTreeNode } from "@/types/repository";

interface DocOption {
  title: string;
  slug: string;
}

function flattenDocNodes(nodes: RepoTreeNode[]): DocOption[] {
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

function findNodeTrail(nodes: RepoTreeNode[], targetSlug: string, trail: string[] = []): string[] | null {
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

function statusBadgeClass(status: string) {
  const value = status.toLowerCase();
  if (value === "completed" || value === "已完成") return "bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200";
  if (value === "processing" || value === "处理中") return "bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-200";
  if (value === "pending" || value === "待处理") return "bg-gray-100 text-gray-800 dark:bg-gray-800 dark:text-gray-200";
  if (value === "failed" || value === "失败") return "bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-200";
  if (value === "cancelled" || value === "已取消") return "bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-200";
  return "bg-muted text-muted-foreground";
}

function mapTaskStatusToAdminTask(
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

function normalizeTaskStatus(status: string) {
  const value = status.toLowerCase();
  if (value.includes("completed") || value.includes("完成")) return "completed";
  if (value.includes("processing") || value.includes("处理")) return "processing";
  if (value.includes("pending") || value.includes("待")) return "pending";
  if (value.includes("failed") || value.includes("失败")) return "failed";
  if (value.includes("cancel") || value.includes("取消")) return "cancelled";
  return "other";
}

function getSourceTypeLabelKey(sourceType: AdminRepository["sourceType"]) {
  switch (sourceType) {
    case "Archive":
      return "sourceTypeArchive";
    case "LocalDirectory":
      return "sourceTypeLocal";
    default:
      return "sourceTypeGit";
  }
}

export default function AdminRepositoryManagementPage() {
  const router = useRouter();
  const t = useTranslations();
  const locale = useLocale();
  const dateLocale = locale === "zh" ? "zh-CN" : locale;
  const params = useParams<{ id: string }>();
  const repositoryId = useMemo(() => {
    const raw = params?.id;
    if (typeof raw === "string") return raw;
    if (Array.isArray(raw)) return raw[0] ?? "";
    return "";
  }, [params]);

  const [repository, setRepository] = useState<AdminRepository | null>(null);
  const [management, setManagement] = useState<AdminRepositoryManagement | null>(null);
  const [logs, setLogs] = useState<ProcessingLogResponse | null>(null);
  const [doc, setDoc] = useState<RepoDocResponse | null>(null);
  const [docOptions, setDocOptions] = useState<DocOption[]>([]);
  const [docTreeNodes, setDocTreeNodes] = useState<RepoTreeNode[]>([]);
  const [expandedDocSlugs, setExpandedDocSlugs] = useState<Set<string>>(new Set());
  const [isEditingDoc, setIsEditingDoc] = useState(false);
  const [docDraft, setDocDraft] = useState("");
  const [savingDoc, setSavingDoc] = useState(false);

  const [selectedBranchId, setSelectedBranchId] = useState("");
  const [selectedLanguage, setSelectedLanguage] = useState("");
  const [selectedDocSlug, setSelectedDocSlug] = useState("");

  const [pageLoading, setPageLoading] = useState(true);
  const [treeLoading, setTreeLoading] = useState(false);
  const [docLoading, setDocLoading] = useState(false);
  const [logsLoading, setLogsLoading] = useState(false);
  const [syncingStats, setSyncingStats] = useState(false);
  const [regeneratingRepo, setRegeneratingRepo] = useState(false);
  const [regeneratingDoc, setRegeneratingDoc] = useState(false);
  const [triggeringIncremental, setTriggeringIncremental] = useState(false);
  const [refreshingAll, setRefreshingAll] = useState(false);
  const [taskRefreshingId, setTaskRefreshingId] = useState<string | null>(null);
  const [taskRetryingId, setTaskRetryingId] = useState<string | null>(null);

  const selectedBranch = useMemo(() => {
    if (!management) return null;
    return management.branches.find((branch) => branch.id === selectedBranchId) ?? null;
  }, [management, selectedBranchId]);

  const selectedLanguageInfo = useMemo(() => {
    if (!selectedBranch) return null;
    return selectedBranch.languages.find((item) => item.languageCode === selectedLanguage) ?? null;
  }, [selectedBranch, selectedLanguage]);

  const isDocDirty = useMemo(
    () => isEditingDoc && doc?.exists && docDraft !== (doc.content ?? ""),
    [isEditingDoc, doc, docDraft]
  );

  const confirmDiscardUnsavedChanges = useCallback(() => {
    if (!isDocDirty) return true;
    return window.confirm(t("admin.repositories.management.confirmDiscardUnsaved"));
  }, [isDocDirty, t]);

  const getLocalizedTaskStatus = useCallback(
    (status: string) => {
      switch (normalizeTaskStatus(status)) {
        case "pending":
          return t("admin.repositories.pending");
        case "processing":
          return t("admin.repositories.processing");
        case "completed":
          return t("admin.repositories.completed");
        case "failed":
          return t("admin.repositories.failed");
        case "cancelled":
          return t("admin.repositories.management.status.cancelled");
        default:
          return status;
      }
    },
    [t]
  );

  const branchLanguageMetrics = useMemo(() => {
    if (!management) {
      return {
        branchCount: 0,
        languageCount: 0,
        totalDocuments: 0,
        totalCatalogs: 0,
        avgDocsPerLanguage: 0,
        branchCoverage: 0,
        defaultLanguageCoverage: 0,
      };
    }

    const branchCount = management.branches.length;
    const languageEntries = management.branches.flatMap((branch) => branch.languages);
    const languageCount = new Set(languageEntries.map((item) => item.languageCode)).size;
    const totalDocuments = languageEntries.reduce((sum, item) => sum + item.documentCount, 0);
    const totalCatalogs = languageEntries.reduce((sum, item) => sum + item.catalogCount, 0);
    const defaultLanguageCount = languageEntries.filter((item) => item.isDefault).length;
    const branchesWithDocs = management.branches.filter((branch) =>
      branch.languages.some((item) => item.documentCount > 0)
    ).length;

    return {
      branchCount,
      languageCount,
      totalDocuments,
      totalCatalogs,
      avgDocsPerLanguage:
        languageEntries.length > 0 ? Number((totalDocuments / languageEntries.length).toFixed(1)) : 0,
      branchCoverage: branchCount > 0 ? Math.round((branchesWithDocs / branchCount) * 100) : 0,
      defaultLanguageCoverage:
        languageEntries.length > 0 ? Math.round((defaultLanguageCount / languageEntries.length) * 100) : 0,
    };
  }, [management]);

  const logProgress = useMemo(() => {
    const total = logs?.totalDocuments ?? 0;
    const completed = logs?.completedDocuments ?? 0;
    return {
      total,
      completed,
      percent: total > 0 ? Math.min(100, Math.round((completed / total) * 100)) : 0,
    };
  }, [logs]);

  const selectedLanguageCoverage = useMemo(() => {
    if (!selectedLanguageInfo || selectedLanguageInfo.catalogCount <= 0) return 0;
    return Math.min(
      100,
      Math.round((selectedLanguageInfo.documentCount / selectedLanguageInfo.catalogCount) * 100)
    );
  }, [selectedLanguageInfo]);

  const supportsGitOperations = repository?.sourceType === "Git";

  const processingFlow = useMemo(() => {
    const steps = [
      { index: 0, label: t("admin.repositories.management.steps.prepareWorkspace") },
      { index: 1, label: t("admin.repositories.management.steps.buildCatalog") },
      { index: 2, label: t("admin.repositories.management.steps.generateDocs") },
      { index: 3, label: t("admin.repositories.management.steps.archiveComplete") },
    ];
    const currentStep = logs?.currentStep ?? -1;
    const failed = (logs?.statusName ?? "").toLowerCase() === "failed";

    return steps.map((step) => {
      if (failed && step.index === currentStep) {
        return { ...step, state: "failed" as const };
      }
      if (step.index < currentStep) {
        return { ...step, state: "done" as const };
      }
      if (step.index === currentStep) {
        return { ...step, state: "active" as const };
      }
      return { ...step, state: "pending" as const };
    });
  }, [logs, t]);

  const incrementalSummary = useMemo(() => {
    const summary = {
      total: 0,
      completed: 0,
      processing: 0,
      pending: 0,
      failed: 0,
      cancelled: 0,
      other: 0,
      successRate: 0,
      activeRate: 0,
    };

    if (!management) {
      return summary;
    }

    summary.total = management.recentIncrementalTasks.length;

    management.recentIncrementalTasks.forEach((task) => {
      const normalizedStatus = normalizeTaskStatus(task.status);
      if (normalizedStatus in summary) {
        (summary[normalizedStatus as keyof typeof summary] as number) += 1;
        return;
      }
      summary.other += 1;
    });

    const terminalCount = summary.completed + summary.failed + summary.cancelled;
    summary.successRate = terminalCount > 0 ? Math.round((summary.completed / terminalCount) * 100) : 0;
    summary.activeRate =
      summary.total > 0 ? Math.round(((summary.processing + summary.pending) / summary.total) * 100) : 0;

    return summary;
  }, [management]);

  const incrementalStatusSegments = useMemo(() => {
    const total = incrementalSummary.total || 1;
    return [
      {
        key: "processing",
        label: t("admin.repositories.processing"),
        count: incrementalSummary.processing,
        percent: Math.round((incrementalSummary.processing / total) * 100),
        color: "bg-blue-500/90",
      },
      {
        key: "pending",
        label: t("admin.repositories.pending"),
        count: incrementalSummary.pending,
        percent: Math.round((incrementalSummary.pending / total) * 100),
        color: "bg-slate-400/90",
      },
      {
        key: "completed",
        label: t("admin.repositories.completed"),
        count: incrementalSummary.completed,
        percent: Math.round((incrementalSummary.completed / total) * 100),
        color: "bg-emerald-500/90",
      },
      {
        key: "failed",
        label: t("admin.repositories.failed"),
        count: incrementalSummary.failed,
        percent: Math.round((incrementalSummary.failed / total) * 100),
        color: "bg-red-500/90",
      },
    ];
  }, [incrementalSummary, t]);

  const loadBaseData = useCallback(async () => {
    if (!repositoryId) {
      return;
    }

    setPageLoading(true);
    try {
      const [repoData, managementData] = await Promise.all([
        getRepository(repositoryId),
        getRepositoryManagement(repositoryId),
      ]);
      setRepository(repoData);
      setManagement(managementData);
    } catch (error) {
      console.error("Failed to load repository management data:", error);
      toast.error(t("admin.repositories.management.toasts.loadManagementFailed"));
    } finally {
      setPageLoading(false);
    }
  }, [repositoryId, t]);

  const loadLogs = useCallback(async () => {
    if (!repository) return;
    setLogsLoading(true);
    try {
      const logData = await fetchProcessingLogs(repository.orgName, repository.repoName, undefined, 200);
      setLogs(logData);
    } catch (error) {
      console.error("Failed to load logs:", error);
      toast.error(t("admin.repositories.management.toasts.loadLogsFailed"));
    } finally {
      setLogsLoading(false);
    }
  }, [repository, t]);

  const loadTree = useCallback(async () => {
    if (!repository || !selectedBranch || !selectedLanguage) {
      setDoc(null);
      setDocOptions([]);
      setDocTreeNodes([]);
      setExpandedDocSlugs(new Set());
      return;
    }

    setTreeLoading(true);
    try {
      const treeData = await fetchRepoTree(
        repository.orgName,
        repository.repoName,
        selectedBranch.name,
        selectedLanguage
      );
      const docs = flattenDocNodes(treeData.nodes);
      setDocTreeNodes(treeData.nodes);
      setExpandedDocSlugs(new Set(treeData.nodes.map((node) => node.slug)));
      setDocOptions(docs);
      setSelectedDocSlug((previous) => {
        if (previous && docs.some((item) => item.slug === previous)) {
          return previous;
        }
        if (treeData.defaultSlug && docs.some((item) => item.slug === treeData.defaultSlug)) {
          return treeData.defaultSlug;
        }
        return docs[0]?.slug ?? "";
      });
    } catch (error) {
      console.error("Failed to load repository tree:", error);
      toast.error(t("admin.repositories.management.toasts.loadDocTreeFailed"));
      setDocOptions([]);
      setDocTreeNodes([]);
      setExpandedDocSlugs(new Set());
      setSelectedDocSlug("");
    } finally {
      setTreeLoading(false);
    }
  }, [repository, selectedBranch, selectedLanguage, t]);

  const loadDoc = useCallback(async () => {
    if (!repository || !selectedBranch || !selectedLanguage || !selectedDocSlug) {
      setDoc(null);
      return;
    }

    setDocLoading(true);
    try {
      const docData = await fetchRepoDoc(
        repository.orgName,
        repository.repoName,
        selectedDocSlug,
        selectedBranch.name,
        selectedLanguage
      );
      setDoc(docData);
    } catch (error) {
      console.error("Failed to load document:", error);
      toast.error(t("admin.repositories.management.toasts.loadDocFailed"));
      setDoc(null);
    } finally {
      setDocLoading(false);
    }
  }, [repository, selectedBranch, selectedLanguage, selectedDocSlug, t]);

  const updateTaskInState = useCallback((task: AdminIncrementalTask) => {
    setManagement((previous) => {
      if (!previous) return previous;
      const index = previous.recentIncrementalTasks.findIndex((item) => item.taskId === task.taskId);
      if (index >= 0) {
        const tasks = [...previous.recentIncrementalTasks];
        tasks[index] = task;
        return { ...previous, recentIncrementalTasks: tasks };
      }
      return {
        ...previous,
        recentIncrementalTasks: [task, ...previous.recentIncrementalTasks].slice(0, 20),
      };
    });
  }, []);

  useEffect(() => {
    loadBaseData();
  }, [loadBaseData]);

  useEffect(() => {
    if (!management || management.branches.length === 0) {
      setSelectedBranchId("");
      return;
    }

    setSelectedBranchId((previous) => {
      if (previous && management.branches.some((branch) => branch.id === previous)) {
        return previous;
      }
      return management.branches[0].id;
    });
  }, [management]);

  useEffect(() => {
    if (!selectedBranch) {
      setSelectedLanguage("");
      return;
    }

    setSelectedLanguage((previous) => {
      if (previous && selectedBranch.languages.some((item) => item.languageCode === previous)) {
        return previous;
      }
      const preferred = selectedBranch.languages.find((item) => item.isDefault);
      return preferred?.languageCode ?? selectedBranch.languages[0]?.languageCode ?? "";
    });
  }, [selectedBranch]);

  useEffect(() => {
    if (repository) {
      loadLogs();
    }
  }, [repository, loadLogs]);

  useEffect(() => {
    loadTree();
  }, [loadTree]);

  useEffect(() => {
    loadDoc();
  }, [loadDoc]);

  useEffect(() => {
    if (!selectedDocSlug || docTreeNodes.length === 0) return;
    const trail = findNodeTrail(docTreeNodes, selectedDocSlug);
    if (!trail) return;
    setExpandedDocSlugs((previous) => {
      const next = new Set(previous);
      trail.forEach((slug) => next.add(slug));
      return next;
    });
  }, [docTreeNodes, selectedDocSlug]);

  useEffect(() => {
    if (doc?.exists) {
      setDocDraft(doc.content);
    } else {
      setDocDraft("");
    }
    setIsEditingDoc(false);
  }, [doc]);

  const toggleDocExpanded = (slug: string) => {
    setExpandedDocSlugs((previous) => {
      const next = new Set(previous);
      if (next.has(slug)) {
        next.delete(slug);
      } else {
        next.add(slug);
      }
      return next;
    });
  };

  const handleSelectDoc = (slug: string) => {
    if (!confirmDiscardUnsavedChanges()) return;
    setSelectedDocSlug(slug);
  };

  const handleBranchChange = (branchId: string) => {
    if (!confirmDiscardUnsavedChanges()) return;
    setSelectedBranchId(branchId);
  };

  const handleLanguageChange = (languageCode: string) => {
    if (!confirmDiscardUnsavedChanges()) return;
    setSelectedLanguage(languageCode);
  };

  const handleRefreshAll = async () => {
    if (!confirmDiscardUnsavedChanges()) return;
    setRefreshingAll(true);
    try {
      await loadBaseData();
      await loadLogs();
      await loadTree();
    } finally {
      setRefreshingAll(false);
    }
  };

  const handleSyncStats = async () => {
    if (!repositoryId) return;
    if (!supportsGitOperations) {
      toast.warning(t("admin.repositories.syncStatsNotSupported"));
      return;
    }
    setSyncingStats(true);
    try {
      const result = await syncRepositoryStats(repositoryId);
      if (result.success) {
        toast.success(
          t("admin.repositories.management.toasts.syncStatsSuccess", {
            star: result.starCount,
            fork: result.forkCount,
          })
        );
        await loadBaseData();
      } else {
        toast.error(result.message || t("admin.repositories.management.toasts.syncStatsFailed"));
      }
    } catch (error) {
      console.error("Failed to sync stats:", error);
      toast.error(t("admin.repositories.management.toasts.syncStatsFailed"));
    } finally {
      setSyncingStats(false);
    }
  };

  const handleRegenerateRepository = async () => {
    if (!repositoryId) return;
    if (!window.confirm(t("admin.repositories.management.confirmRegenerateAll"))) return;

    setRegeneratingRepo(true);
    try {
      const result = await regenerateRepository(repositoryId);
      if (result.success) {
        toast.success(result.message || t("admin.repositories.management.toasts.regenerateAllSuccess"));
        await loadBaseData();
        await loadLogs();
      } else {
        toast.error(result.message || t("admin.repositories.management.toasts.regenerateAllFailed"));
      }
    } catch (error) {
      console.error("Failed to regenerate repository:", error);
      toast.error(t("admin.repositories.management.toasts.regenerateAllFailed"));
    } finally {
      setRegeneratingRepo(false);
    }
  };

  const handleRegenerateDocument = async () => {
    if (!repositoryId || !selectedBranch || !selectedLanguage || !selectedDocSlug) {
      toast.warning(t("admin.repositories.management.toasts.selectBranchLanguageDocFirst"));
      return;
    }

    if (!confirmDiscardUnsavedChanges()) return;

    if (!window.confirm(t("admin.repositories.management.confirmRegenerateDoc", { doc: selectedDocSlug }))) return;

    setRegeneratingDoc(true);
    try {
      const result = await regenerateRepositoryDocument(repositoryId, {
        branchId: selectedBranch.id,
        languageCode: selectedLanguage,
        documentPath: selectedDocSlug,
      });

      if (result.success) {
        toast.success(result.message || t("admin.repositories.management.toasts.regenerateDocSuccess"));
        await loadDoc();
        await loadLogs();
      } else {
        toast.error(result.message || t("admin.repositories.management.toasts.regenerateDocFailed"));
      }
    } catch (error) {
      console.error("Failed to regenerate document:", error);
      toast.error(t("admin.repositories.management.toasts.regenerateDocFailed"));
    } finally {
      setRegeneratingDoc(false);
    }
  };

  const handleStartEditDoc = () => {
    if (!doc?.exists) {
      toast.warning(t("admin.repositories.management.toasts.docNotExistsCannotEdit"));
      return;
    }
    setDocDraft(doc.content);
    setIsEditingDoc(true);
  };

  const handleCancelEditDoc = () => {
    if (isDocDirty && !window.confirm(t("admin.repositories.management.confirmDiscardEdit"))) {
      return;
    }
    setDocDraft(doc?.content ?? "");
    setIsEditingDoc(false);
  };

  const handleSaveDocContent = async () => {
    if (!repositoryId || !selectedBranch || !selectedLanguage || !selectedDocSlug) {
      toast.warning(t("admin.repositories.management.toasts.selectBranchLanguageDocFirst"));
      return;
    }

    setSavingDoc(true);
    try {
      const result = await updateRepositoryDocumentContent(repositoryId, {
        branchId: selectedBranch.id,
        languageCode: selectedLanguage,
        documentPath: selectedDocSlug,
        content: docDraft,
      });

      if (result.success) {
        toast.success(result.message || t("admin.repositories.management.toasts.saveDocSuccess"));
        setDoc((previous) =>
          previous
            ? {
                ...previous,
                content: docDraft,
              }
            : previous
        );
        setIsEditingDoc(false);
        await loadLogs();
      } else {
        toast.error(result.message || t("admin.repositories.management.toasts.saveDocFailed"));
      }
    } catch (error) {
      console.error("Failed to update document content:", error);
      toast.error(t("admin.repositories.management.toasts.saveDocFailed"));
    } finally {
      setSavingDoc(false);
    }
  };

  const handleTriggerIncremental = async () => {
    if (!repositoryId || !selectedBranch) {
      toast.warning(t("admin.repositories.management.toasts.selectBranchFirst"));
      return;
    }
    if (!supportsGitOperations) {
      toast.warning(t("admin.repositories.management.incrementalNotSupported"));
      return;
    }

    setTriggeringIncremental(true);
    try {
      const result = await triggerRepositoryIncrementalUpdate(repositoryId, selectedBranch.id);
      if (result.success) {
        toast.success(t("admin.repositories.management.toasts.triggerIncrementalSuccess", { taskId: result.taskId }));
        await loadBaseData();
      } else {
        toast.error(result.message || t("admin.repositories.management.toasts.triggerIncrementalFailed"));
      }
    } catch (error) {
      console.error("Failed to trigger incremental update:", error);
      toast.error(t("admin.repositories.management.toasts.triggerIncrementalFailed"));
    } finally {
      setTriggeringIncremental(false);
    }
  };

  const handleRefreshTask = async (taskId: string) => {
    setTaskRefreshingId(taskId);
    try {
      const taskStatus = await getIncrementalUpdateTask(taskId);
      if (taskStatus.success) {
        updateTaskInState(mapTaskStatusToAdminTask(taskStatus));
      } else {
        toast.error(t("admin.repositories.management.toasts.refreshTaskFailed"));
      }
    } catch (error) {
      console.error("Failed to refresh task:", error);
      toast.error(t("admin.repositories.management.toasts.refreshTaskFailed"));
    } finally {
      setTaskRefreshingId(null);
    }
  };

  const handleRetryTask = async (taskId: string) => {
    setTaskRetryingId(taskId);
    try {
      const result = await retryIncrementalUpdateTask(taskId);
      if (result.success) {
        toast.success(result.message || t("admin.repositories.management.toasts.retryTaskSuccess"));
        await handleRefreshTask(taskId);
      } else {
        toast.error(result.message || t("admin.repositories.management.toasts.retryTaskFailed"));
      }
    } catch (error) {
      console.error("Failed to retry task:", error);
      toast.error(t("admin.repositories.management.toasts.retryTaskFailed"));
    } finally {
      setTaskRetryingId(null);
    }
  };

  const renderDocTreeNodes = (nodes: RepoTreeNode[], depth = 0): React.ReactNode => {
    return nodes.map((node, index) => {
      const hasChildren = node.children.length > 0;
      const isExpanded = expandedDocSlugs.has(node.slug);
      const isActive = node.slug === selectedDocSlug;

      return (
        <div key={`${node.slug}-${depth}`} className="space-y-1">
          <div
            className={`flex items-center gap-1 rounded px-1 py-1 transition-all duration-200 ${
              isActive ? "bg-primary/10 text-primary ring-1 ring-primary/30" : "hover:bg-muted"
            }`}
            style={{ paddingLeft: depth * 14 + 4, animationDelay: `${Math.min(index * 16, 140)}ms` }}
          >
            <button
              type="button"
              className={`flex h-5 w-5 items-center justify-center rounded transition-colors ${
                hasChildren ? "hover:bg-muted-foreground/10" : "opacity-40"
              }`}
              onClick={() => {
                if (!hasChildren) return;
                toggleDocExpanded(node.slug);
              }}
            >
              {hasChildren ? (
                isExpanded ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />
              ) : (
                <span className="h-1.5 w-1.5 rounded-full bg-muted-foreground/60" />
              )}
            </button>
            <button
              type="button"
              className={`flex-1 truncate text-left text-sm ${hasChildren ? "cursor-pointer" : ""}`}
              onClick={() => {
                if (hasChildren) {
                  toggleDocExpanded(node.slug);
                } else {
                  handleSelectDoc(node.slug);
                }
              }}
              title={node.slug}
            >
              {node.title}
            </button>
          </div>

          {hasChildren && isExpanded && (
            <div className="animate-in fade-in-0 slide-in-from-top-1 duration-200">
              {renderDocTreeNodes(node.children, depth + 1)}
            </div>
          )}
        </div>
      );
    });
  };

  if (pageLoading) {
    return (
      <div className="flex h-[60vh] items-center justify-center animate-in fade-in-0 duration-300">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!repository || !management) {
    return (
      <div className="space-y-4 animate-in fade-in-0 slide-in-from-bottom-2 duration-300">
        <Button
          variant="outline"
          onClick={() => router.push("/admin/repositories")}
          className="transition-all duration-200 hover:-translate-y-0.5"
        >
          <ArrowLeft className="mr-2 h-4 w-4" />
          {t("admin.repositories.management.backToList")}
        </Button>
        <Card className="p-6 text-center text-muted-foreground">{t("admin.repositories.management.notFound")}</Card>
      </div>
    );
  }

  return (
    <div className="space-y-6 animate-in fade-in-0 duration-500">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="space-y-1">
          <Button
            variant="ghost"
            size="sm"
            onClick={() => router.push("/admin/repositories")}
            className="transition-all duration-200 hover:-translate-x-1"
          >
            <ArrowLeft className="mr-2 h-4 w-4" />
            {t("admin.repositories.management.backToList")}
          </Button>
          <h1 className="text-2xl font-bold">{repository.orgName}/{repository.repoName}</h1>
          <div className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <span className="inline-flex rounded-full bg-secondary px-2 py-1 text-xs">
              {t(`admin.repositories.${getSourceTypeLabelKey(repository.sourceType)}`)}
            </span>
            <span className="break-all">{repository.sourceLocation || repository.gitUrl}</span>
          </div>
          <p className="inline-flex items-center gap-2 text-xs text-muted-foreground">
            <Sparkles className="h-3.5 w-3.5 text-primary" />
            {t("admin.repositories.management.visualHint")}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" onClick={handleRefreshAll} disabled={refreshingAll}>
            <RefreshCw className={`mr-2 h-4 w-4 ${refreshingAll ? "animate-spin" : ""}`} />
            {t("admin.repositories.management.refresh")}
          </Button>
          <Button variant="outline" onClick={handleSyncStats} disabled={syncingStats || !supportsGitOperations} title={supportsGitOperations ? t("admin.repositories.management.syncStats") : t("admin.repositories.syncStatsNotSupported")}>
            {syncingStats ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <RotateCcw className="mr-2 h-4 w-4" />}
            {t("admin.repositories.management.syncStats")}
          </Button>
          <Button variant="outline" onClick={handleTriggerIncremental} disabled={triggeringIncremental || !selectedBranch || !supportsGitOperations} title={supportsGitOperations ? t("admin.repositories.management.triggerIncremental") : t("admin.repositories.management.incrementalNotSupported")}>
            {triggeringIncremental ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Zap className="mr-2 h-4 w-4" />}
            {t("admin.repositories.management.triggerIncremental")}
          </Button>
          <Button variant="destructive" onClick={handleRegenerateRepository} disabled={regeneratingRepo}>
            {regeneratingRepo ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <RefreshCw className="mr-2 h-4 w-4" />}
            {t("admin.repositories.management.regenerateAll")}
          </Button>
        </div>
      </div>

      <Card className="p-4 transition-all duration-300 hover:shadow-sm">
        <div className="grid gap-4 xl:grid-cols-[1.2fr_1fr]">
          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <p className="text-sm font-semibold">{t("admin.repositories.management.summaryTitle")}</p>
              <span className={`inline-flex rounded px-2 py-1 text-xs ${statusBadgeClass(repository.statusText)}`}>
                {getLocalizedTaskStatus(repository.statusText)}
              </span>
            </div>
            <div className="rounded-lg border bg-muted/30 p-3">
              <div className="mb-2 flex items-center justify-between text-xs text-muted-foreground">
                <span>{t("admin.repositories.management.branchCoverage")}</span>
                <span>{branchLanguageMetrics.branchCoverage}%</span>
              </div>
              <Progress value={branchLanguageMetrics.branchCoverage} className="h-2.5" />
            </div>
            <div className="rounded-lg border bg-muted/30 p-3">
              <div className="mb-2 flex items-center justify-between text-xs text-muted-foreground">
                <span>{t("admin.repositories.management.defaultLanguageCoverage")}</span>
                <span>{branchLanguageMetrics.defaultLanguageCoverage}%</span>
              </div>
              <Progress value={branchLanguageMetrics.defaultLanguageCoverage} className="h-2.5" />
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <Card className="p-3 transition-all duration-300 hover:-translate-y-0.5 hover:shadow-sm">
              <p className="text-xs text-muted-foreground">{t("admin.repositories.management.branchLanguage")}</p>
              <p className="text-xl font-semibold">
                {branchLanguageMetrics.branchCount} / {branchLanguageMetrics.languageCount}
              </p>
            </Card>
            <Card className="p-3 transition-all duration-300 hover:-translate-y-0.5 hover:shadow-sm">
              <p className="text-xs text-muted-foreground">{t("admin.repositories.management.docCatalog")}</p>
              <p className="text-xl font-semibold">
                {branchLanguageMetrics.totalDocuments} / {branchLanguageMetrics.totalCatalogs}
              </p>
            </Card>
            <Card className="p-3 transition-all duration-300 hover:-translate-y-0.5 hover:shadow-sm">
              <p className="text-xs text-muted-foreground">Star / Fork</p>
              <p className="text-xl font-semibold">
                {supportsGitOperations
                  ? `${repository.starCount} / ${repository.forkCount}`
                  : t("admin.repositories.management.notAvailable")}
              </p>
            </Card>
            <Card className="p-3 transition-all duration-300 hover:-translate-y-0.5 hover:shadow-sm">
              <p className="text-xs text-muted-foreground">{t("admin.repositories.management.avgDocsPerLanguage")}</p>
              <p className="text-xl font-semibold">{branchLanguageMetrics.avgDocsPerLanguage}</p>
            </Card>
          </div>
        </div>
      </Card>

      <Tabs defaultValue="docs" className="w-full">
        <TabsList className="grid w-full grid-cols-2 gap-2 md:grid-cols-4">
          <TabsTrigger value="branches" className="transition-all data-[state=active]:shadow-sm">
            <GitBranch className="mr-2 h-4 w-4" />
            {t("admin.repositories.management.tabs.branches")}
            <span className="ml-1 rounded bg-muted px-1.5 text-[10px] leading-4">
              {management.branches.length}
            </span>
          </TabsTrigger>
          <TabsTrigger value="docs" className="transition-all data-[state=active]:shadow-sm">
            <FileText className="mr-2 h-4 w-4" />
            {t("admin.repositories.management.tabs.docs")}
            <span className="ml-1 rounded bg-muted px-1.5 text-[10px] leading-4">{docOptions.length}</span>
          </TabsTrigger>
          <TabsTrigger value="logs" className="transition-all data-[state=active]:shadow-sm">
            <History className="mr-2 h-4 w-4" />
            {t("admin.repositories.management.tabs.logs")}
            <span className="ml-1 rounded bg-muted px-1.5 text-[10px] leading-4">{logs?.logs.length ?? 0}</span>
          </TabsTrigger>
          <TabsTrigger value="incremental" className="transition-all data-[state=active]:shadow-sm">
            <Zap className="mr-2 h-4 w-4" />
            {t("admin.repositories.management.tabs.incremental")}
            <span className="ml-1 rounded bg-muted px-1.5 text-[10px] leading-4">
              {management.recentIncrementalTasks.length}
            </span>
          </TabsTrigger>
        </TabsList>

        <TabsContent value="branches" className="mt-4 animate-in fade-in-0 slide-in-from-bottom-2 duration-300">
          <div className="grid gap-4 lg:grid-cols-2">
            {management.branches.map((branch, index) => {
              const isSelected = selectedBranchId === branch.id;
              return (
                <button
                  key={branch.id}
                  type="button"
                  className="text-left"
                  onClick={() => handleBranchChange(branch.id)}
                >
                  <Card
                    className={`p-4 animate-in fade-in-0 slide-in-from-bottom-1 transition-all duration-300 hover:-translate-y-0.5 hover:shadow-sm ${
                      isSelected ? "ring-1 ring-primary/40 bg-primary/5" : ""
                    }`}
                    style={{ animationDelay: `${Math.min(index * 35, 180)}ms` }}
                  >
                    <div className="mb-3 flex items-center justify-between">
                      <h3 className="font-semibold">{branch.name}</h3>
                      {isSelected && <Badge>{t("admin.repositories.management.branchSelected")}</Badge>}
                    </div>
                    <div className="space-y-2 text-sm">
                      <p className="text-muted-foreground">
                        {t("admin.repositories.management.lastCommit")}: {branch.lastCommitId || t("admin.repositories.management.notAvailable")}
                      </p>
                      <p className="text-muted-foreground">
                        {t("admin.repositories.management.lastProcessed")}:{" "}
                        {branch.lastProcessedAt ? new Date(branch.lastProcessedAt).toLocaleString(dateLocale) : t("admin.repositories.management.notAvailable")}
                      </p>
                    </div>
                    <div className="mt-3 flex flex-wrap gap-2">
                      {branch.languages.map((language) => (
                        <Badge key={language.id} variant={language.isDefault ? "default" : "secondary"}>
                          <Languages className="mr-1 h-3 w-3" />
                          {language.languageCode}
                          <span className="ml-1 text-[10px] opacity-80">
                            ({language.documentCount}/{language.catalogCount})
                          </span>
                        </Badge>
                      ))}
                    </div>
                  </Card>
                </button>
              );
            })}
            {management.branches.length === 0 && (
              <Card className="p-6 text-center text-muted-foreground animate-in fade-in-0 duration-300">
                {t("admin.repositories.management.noManageableBranches")}
              </Card>
            )}
          </div>
        </TabsContent>

        <TabsContent value="docs" className="mt-4 space-y-4 animate-in fade-in-0 slide-in-from-bottom-2 duration-300">
          <Card className="p-4 transition-all duration-300 hover:shadow-sm">
            <div className="grid gap-3 md:grid-cols-4">
              <div>
                <p className="mb-2 text-xs text-muted-foreground">{t("admin.repositories.management.filters.branch")}</p>
                <Select value={selectedBranchId} onValueChange={handleBranchChange}>
                  <SelectTrigger>
                    <SelectValue placeholder={t("admin.repositories.management.filters.selectBranch")} />
                  </SelectTrigger>
                  <SelectContent>
                    {management.branches.map((branch) => (
                      <SelectItem key={branch.id} value={branch.id}>
                        {branch.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div>
                <p className="mb-2 text-xs text-muted-foreground">{t("admin.repositories.management.filters.language")}</p>
                <Select value={selectedLanguage} onValueChange={handleLanguageChange} disabled={!selectedBranch}>
                  <SelectTrigger>
                    <SelectValue placeholder={t("admin.repositories.management.filters.selectLanguage")} />
                  </SelectTrigger>
                  <SelectContent>
                    {(selectedBranch?.languages ?? []).map((language) => (
                      <SelectItem key={language.id} value={language.languageCode}>
                        {language.languageCode}{language.isDefault ? ` ${t("admin.repositories.management.filters.defaultSuffix")}` : ""}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="md:col-span-2 flex items-end gap-2">
                <Button
                  variant="outline"
                  onClick={() => {
                    if (!confirmDiscardUnsavedChanges()) return;
                    loadTree();
                  }}
                  disabled={treeLoading}
                  className="transition-all duration-200"
                >
                  {treeLoading ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <RefreshCw className="mr-2 h-4 w-4" />}
                  {t("admin.repositories.management.refreshDocTree")}
                </Button>
                <Button
                  onClick={handleRegenerateDocument}
                  disabled={!selectedDocSlug || regeneratingDoc || !selectedLanguageInfo}
                  className="transition-all duration-200 hover:-translate-y-0.5"
                >
                  {regeneratingDoc ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <RotateCcw className="mr-2 h-4 w-4" />}
                  {t("admin.repositories.management.regenerateCurrentDoc")}
                </Button>
              </div>
            </div>
            <div className="mt-4 grid gap-3 md:grid-cols-3">
              <Card className="p-3">
                <p className="text-xs text-muted-foreground">{t("admin.repositories.management.loadedNodes")}</p>
                <p className="text-lg font-semibold">{docOptions.length}</p>
              </Card>
              <Card className="p-3">
                <p className="text-xs text-muted-foreground">{t("admin.repositories.management.currentLanguageDocCatalog")}</p>
                <p className="text-lg font-semibold">
                  {selectedLanguageInfo
                    ? `${selectedLanguageInfo.documentCount} / ${selectedLanguageInfo.catalogCount}`
                    : t("admin.repositories.management.notSelected")}
                </p>
              </Card>
              <Card className="p-3">
                <p className="mb-2 text-xs text-muted-foreground">{t("admin.repositories.management.currentLanguageCoverage")}</p>
                <div className="mb-1 flex items-center justify-between text-xs text-muted-foreground">
                  <span>{t("admin.repositories.management.coverage")}</span>
                  <span>{selectedLanguageCoverage}%</span>
                </div>
                <Progress value={selectedLanguageCoverage} className="h-2.5" />
              </Card>
            </div>
          </Card>

          <div className="grid gap-4 lg:grid-cols-[300px_1fr]">
            <Card className="p-3 transition-all duration-300 hover:shadow-sm">
              <div className="mb-3 flex items-center justify-between">
                <h3 className="text-sm font-semibold">{t("admin.repositories.management.docTree")}</h3>
                <Badge variant="outline">{t("admin.repositories.management.nodeCount", { count: docOptions.length })}</Badge>
              </div>
              <div className="max-h-[560px] overflow-auto space-y-1 pr-1">
                {docTreeNodes.length > 0 ? (
                  renderDocTreeNodes(docTreeNodes)
                ) : (
                  <p className="text-sm text-muted-foreground">{t("admin.repositories.management.noManageableDocs")}</p>
                )}
              </div>
            </Card>

            <Card className="p-4 transition-all duration-300 hover:shadow-sm">
              <div className="mb-3 flex items-center justify-between">
                <div>
                  <h3 className="font-semibold">{t("admin.repositories.management.docContent")}</h3>
                  <p className="text-xs text-muted-foreground">{selectedDocSlug || t("admin.repositories.management.noSelectedDoc")}</p>
                </div>
                <div className="flex items-center gap-2">
                  {isDocDirty && <Badge variant="destructive">{t("admin.repositories.management.unsaved")}</Badge>}
                  {selectedLanguageInfo && (
                    <Badge variant="secondary">
                      {selectedLanguageInfo.languageCode} · {t("admin.repositories.management.docCount", { count: selectedLanguageInfo.documentCount })}
                    </Badge>
                  )}
                </div>
              </div>
              {docLoading ? (
                <div className="flex h-[520px] items-center justify-center animate-in fade-in-0 duration-200">
                  <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
                </div>
              ) : doc?.exists ? (
                <div className="space-y-3">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="flex flex-wrap gap-2">
                      <Badge variant="outline">{t("admin.repositories.management.sourceFiles", { count: doc.sourceFiles.length })}</Badge>
                      {doc.sourceFiles.slice(0, 2).map((filePath) => (
                        <Badge key={filePath} variant="secondary" className="max-w-[280px] truncate">
                          {filePath}
                        </Badge>
                      ))}
                    </div>
                    <div className="flex items-center gap-2">
                      {!isEditingDoc ? (
                        <Button variant="outline" size="sm" onClick={handleStartEditDoc}>
                          <Pencil className="mr-2 h-4 w-4" />
                          {t("admin.repositories.management.editDoc")}
                        </Button>
                      ) : (
                        <>
                          <Button
                            size="sm"
                            onClick={handleSaveDocContent}
                            disabled={!isDocDirty || savingDoc}
                          >
                            {savingDoc ? (
                              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                            ) : (
                              <Save className="mr-2 h-4 w-4" />
                            )}
                            {t("admin.repositories.management.saveDoc")}
                          </Button>
                          <Button variant="outline" size="sm" onClick={handleCancelEditDoc} disabled={savingDoc}>
                            <X className="mr-2 h-4 w-4" />
                            {t("admin.repositories.management.cancelEdit")}
                          </Button>
                        </>
                      )}
                    </div>
                  </div>
                  {!isEditingDoc ? (
                    <pre
                      key={selectedDocSlug}
                      className="max-h-[520px] overflow-auto whitespace-pre-wrap rounded bg-muted p-4 text-xs leading-6 animate-in fade-in-0 duration-200"
                    >
                      {doc.content}
                    </pre>
                  ) : (
                    <div className="space-y-2">
                      <textarea
                        value={docDraft}
                        onChange={(event) => setDocDraft(event.target.value)}
                        className="h-[520px] w-full resize-none rounded-md border bg-background p-3 text-xs leading-6 outline-none focus-visible:ring-2 focus-visible:ring-ring"
                      />
                      <div className="flex items-center justify-between text-xs text-muted-foreground">
                        <span>{t("admin.repositories.management.editHint")}</span>
                        <span>{t("admin.repositories.management.charCount", { count: docDraft.length })}</span>
                      </div>
                    </div>
                  )}
                </div>
              ) : (
                <div className="flex h-[520px] items-center justify-center text-sm text-muted-foreground animate-in fade-in-0 duration-200">
                  {t("admin.repositories.management.noDocContent")}
                </div>
              )}
            </Card>
          </div>
        </TabsContent>

        <TabsContent value="logs" className="mt-4 space-y-4 animate-in fade-in-0 slide-in-from-bottom-2 duration-300">
          <Card className="p-4 transition-all duration-300 hover:shadow-sm">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="space-y-1">
                <h3 className="font-semibold">{t("admin.repositories.management.logsTitle")}</h3>
                {logs && (
                  <p className="text-xs text-muted-foreground">
                    {t("admin.repositories.management.currentStepProgress", {
                      step: logs.currentStepName,
                      completed: logs.completedDocuments,
                      total: logs.totalDocuments,
                    })}
                  </p>
                )}
              </div>
              <Button variant="outline" onClick={loadLogs} disabled={logsLoading} className="transition-all duration-200">
                {logsLoading ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <RefreshCw className="mr-2 h-4 w-4" />}
                {t("admin.repositories.management.refreshLogs")}
              </Button>
            </div>
            <div className="mt-3 rounded-lg border bg-muted/30 p-3">
              <div className="mb-2 flex items-center justify-between text-xs text-muted-foreground">
                <span className="inline-flex items-center gap-1">
                  <Clock3 className="h-3.5 w-3.5" />
                  {t("admin.repositories.management.logProgress")}
                </span>
                <span>{logProgress.completed} / {logProgress.total}</span>
              </div>
              <Progress value={logProgress.percent} className="h-2.5" />
            </div>
            <div className="mt-3 grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
              {processingFlow.map((step) => (
                <div
                  key={step.index}
                  className={`rounded-md border px-3 py-2 text-xs transition-all ${
                    step.state === "done"
                      ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-700"
                      : step.state === "active"
                        ? "border-blue-500/30 bg-blue-500/10 text-blue-700 ring-1 ring-blue-500/20"
                        : step.state === "failed"
                          ? "border-red-500/30 bg-red-500/10 text-red-700"
                          : "text-muted-foreground"
                  }`}
                >
                  <div className="flex items-center justify-between">
                    <span>{step.label}</span>
                    <span className="text-[10px]">
                      {step.state === "done"
                        ? t("admin.repositories.management.stepState.done")
                        : step.state === "active"
                          ? t("admin.repositories.management.stepState.active")
                          : step.state === "failed"
                            ? t("admin.repositories.management.stepState.failed")
                            : t("admin.repositories.management.stepState.pending")}
                    </span>
                  </div>
                </div>
              ))}
            </div>
          </Card>

          <Card className="p-0 overflow-hidden transition-all duration-300 hover:shadow-sm">
            <div className="max-h-[560px] overflow-auto">
              <table className="w-full">
                <thead className="sticky top-0 bg-muted/80 backdrop-blur border-b">
                  <tr>
                    <th className="px-4 py-3 text-left text-xs font-medium">{t("admin.repositories.management.logColumns.time")}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium">{t("admin.repositories.management.logColumns.step")}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium">{t("admin.repositories.management.logColumns.type")}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium">{t("admin.repositories.management.logColumns.message")}</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {(logs?.logs ?? []).map((log, index) => (
                    <tr
                      key={log.id}
                      className="animate-in fade-in-0 slide-in-from-bottom-1 transition-colors hover:bg-muted/40"
                      style={{ animationDelay: `${Math.min(index * 18, 180)}ms` }}
                    >
                      <td className="px-4 py-2 text-xs text-muted-foreground whitespace-nowrap">
                        {new Date(log.createdAt).toLocaleString(dateLocale)}
                      </td>
                      <td className="px-4 py-2 text-xs">{log.stepName}</td>
                      <td className="px-4 py-2 text-xs">
                        {log.isAiOutput ? <Badge variant="secondary">AI</Badge> : <Badge variant="outline">{t("admin.repositories.management.logTypeSystem")}</Badge>}
                      </td>
                      <td className="px-4 py-2 text-xs">{log.message}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {(logs?.logs.length ?? 0) === 0 && (
                <div className="p-8 text-center text-sm text-muted-foreground">{t("admin.repositories.management.noLogs")}</div>
              )}
            </div>
          </Card>
        </TabsContent>

        <TabsContent value="incremental" className="mt-4 space-y-4 animate-in fade-in-0 slide-in-from-bottom-2 duration-300">
          <Card className="p-4 transition-all duration-300 hover:shadow-sm">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="space-y-1">
                <h3 className="font-semibold">{t("admin.repositories.management.incrementalTitle")}</h3>
                <p className="text-xs text-muted-foreground">
                  {t("admin.repositories.management.incrementalSubtitle", {
                    branch: selectedBranch?.name ?? t("admin.repositories.management.notSelected"),
                  })}
                </p>
              </div>
              <Button
                onClick={handleTriggerIncremental}
                disabled={triggeringIncremental || !selectedBranch || !supportsGitOperations}
                title={supportsGitOperations ? t("admin.repositories.management.triggerCurrentBranchIncremental") : t("admin.repositories.management.incrementalNotSupported")}
                className="transition-all duration-200 hover:-translate-y-0.5"
              >
                {triggeringIncremental ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Zap className="mr-2 h-4 w-4" />}
                {t("admin.repositories.management.triggerCurrentBranchIncremental")}
              </Button>
            </div>

            {!supportsGitOperations && (
              <p className="text-xs text-muted-foreground">
                {t("admin.repositories.management.incrementalNotSupported")}
              </p>
            )}

            <div className="mt-4 grid gap-3 md:grid-cols-3">
              <Card className="p-3">
                <div className="mb-2 flex items-center justify-between text-xs text-muted-foreground">
                  <span className="inline-flex items-center gap-1">
                    <CheckCircle2 className="h-3.5 w-3.5 text-green-500" />
                    {t("admin.repositories.management.successRate")}
                  </span>
                  <span>{incrementalSummary.successRate}%</span>
                </div>
                <Progress value={incrementalSummary.successRate} className="h-2.5" />
              </Card>
              <Card className="p-3">
                <div className="mb-2 flex items-center justify-between text-xs text-muted-foreground">
                  <span className="inline-flex items-center gap-1">
                    <Clock3 className="h-3.5 w-3.5 text-blue-500" />
                    {t("admin.repositories.management.activeRate")}
                  </span>
                  <span>{incrementalSummary.activeRate}%</span>
                </div>
                <Progress value={incrementalSummary.activeRate} className="h-2.5" />
              </Card>
              <Card className="p-3">
                <p className="text-xs text-muted-foreground">{t("admin.repositories.management.failedAlerts")}</p>
                <p className="mt-1 inline-flex items-center gap-1 text-lg font-semibold">
                  <AlertTriangle className="h-4 w-4 text-amber-500" />
                  {incrementalSummary.failed}
                </p>
              </Card>
            </div>

            <div className="mt-3 flex flex-wrap gap-2">
              <Badge variant="outline">{t("admin.repositories.management.totalTasks", { count: incrementalSummary.total })}</Badge>
              <Badge variant="secondary">{t("admin.repositories.management.processingTasks", { count: incrementalSummary.processing })}</Badge>
              <Badge variant="secondary">{t("admin.repositories.management.pendingTasks", { count: incrementalSummary.pending })}</Badge>
              <Badge variant="secondary">{t("admin.repositories.management.completedTasks", { count: incrementalSummary.completed })}</Badge>
              <Badge variant="destructive">{t("admin.repositories.management.failedTasks", { count: incrementalSummary.failed })}</Badge>
            </div>
            <div className="mt-3 rounded-lg border bg-muted/30 p-3">
              <div className="mb-2 flex items-center justify-between text-xs text-muted-foreground">
                <span>{t("admin.repositories.management.taskStatusDistribution")}</span>
                <span>{t("admin.repositories.management.taskRecordCount", { count: incrementalSummary.total })}</span>
              </div>
              <div className="h-2.5 w-full overflow-hidden rounded-full bg-muted">
                <div className="flex h-full w-full">
                  {incrementalStatusSegments.map((segment) => (
                    <div
                      key={segment.key}
                      className={`h-full transition-all duration-500 ${segment.color}`}
                      style={{ width: `${segment.percent}%` }}
                    />
                  ))}
                </div>
              </div>
              <div className="mt-2 flex flex-wrap gap-2">
                {incrementalStatusSegments.map((segment) => (
                  <Badge key={segment.key} variant="secondary" className="gap-1">
                    <span className={`inline-block h-2 w-2 rounded-full ${segment.color}`} />
                    {segment.label} {segment.count}
                  </Badge>
                ))}
              </div>
            </div>
          </Card>

          <Card className="p-0 overflow-hidden transition-all duration-300 hover:shadow-sm">
            <div className="max-h-[560px] overflow-auto">
              <table className="w-full">
                <thead className="sticky top-0 bg-muted/80 backdrop-blur border-b">
                  <tr>
                    <th className="px-4 py-3 text-left text-xs font-medium">{t("admin.repositories.management.taskColumns.taskId")}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium">{t("admin.repositories.management.taskColumns.branch")}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium">{t("admin.repositories.management.taskColumns.status")}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium">{t("admin.repositories.management.taskColumns.createdAt")}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium">{t("admin.repositories.management.taskColumns.retry")}</th>
                    <th className="px-4 py-3 text-right text-xs font-medium">{t("admin.repositories.management.taskColumns.actions")}</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {management.recentIncrementalTasks.map((task, index) => (
                    <tr
                      key={task.taskId}
                      className="animate-in fade-in-0 slide-in-from-bottom-1 transition-colors hover:bg-muted/40"
                      style={{ animationDelay: `${Math.min(index * 22, 220)}ms` }}
                    >
                      <td className="px-4 py-2 text-xs font-mono max-w-[220px] truncate">{task.taskId}</td>
                      <td className="px-4 py-2 text-xs">{task.branchName || task.branchId}</td>
                      <td className="px-4 py-2 text-xs">
                        <span className={`inline-flex rounded px-2 py-1 text-xs ${statusBadgeClass(task.status)}`}>
                          {getLocalizedTaskStatus(task.status)}
                        </span>
                        {task.errorMessage && (
                          <p className="mt-1 max-w-[260px] truncate text-[11px] text-red-500">{task.errorMessage}</p>
                        )}
                      </td>
                      <td className="px-4 py-2 text-xs whitespace-nowrap">{new Date(task.createdAt).toLocaleString(dateLocale)}</td>
                      <td className="px-4 py-2 text-xs">{task.retryCount}</td>
                      <td className="px-4 py-2">
                        <div className="flex justify-end gap-2">
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => handleRefreshTask(task.taskId)}
                            disabled={taskRefreshingId === task.taskId}
                          >
                            {taskRefreshingId === task.taskId ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
                          </Button>
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => handleRetryTask(task.taskId)}
                            disabled={taskRetryingId === task.taskId || normalizeTaskStatus(task.status) !== "failed"}
                            title={
                              normalizeTaskStatus(task.status) === "failed"
                                ? t("admin.repositories.management.retryFailedTask")
                                : t("admin.repositories.management.retryOnlyFailed")
                            }
                          >
                            {taskRetryingId === task.taskId ? <Loader2 className="h-4 w-4 animate-spin" /> : <RotateCcw className="h-4 w-4" />}
                          </Button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {management.recentIncrementalTasks.length === 0 && (
                <div className="p-8 text-center text-sm text-muted-foreground animate-in fade-in-0 duration-200">
                  {t("admin.repositories.management.noIncrementalTasks")}
                </div>
              )}
            </div>
          </Card>
        </TabsContent>
      </Tabs>
    </div>
  );
}
