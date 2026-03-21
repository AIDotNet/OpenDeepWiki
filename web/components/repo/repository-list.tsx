"use client";

import { useEffect, useState, useCallback } from "react";
import Link from "next/link";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { useTranslations } from "@/hooks/use-translations";
import { fetchRepositoryList, regenerateRepository } from "@/lib/repository-api";
import type {
  RepositoryItemResponse,
  RepositorySourceType,
  RepositoryStatus,
} from "@/types/repository";
import {
  Clock,
  Loader2,
  CheckCircle2,
  XCircle,
  ExternalLink,
  RefreshCw,
  GitBranch,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { buildRepoBasePath } from "@/lib/repo-route";
import { VisibilityToggle } from "@/components/repo/visibility-toggle";
import { toast } from "sonner";

interface RepositoryListProps {
  ownerId?: string;
  refreshTrigger?: number;
}

const STATUS_CONFIG: Record<RepositoryStatus, {
  icon: typeof Clock;
  className: string;
  labelKey: string;
}> = {
  Pending: {
    icon: Clock,
    className: "text-yellow-500 bg-yellow-500/10",
    labelKey: "pending",
  },
  Processing: {
    icon: Loader2,
    className: "text-blue-500 bg-blue-500/10",
    labelKey: "processing",
  },
  Completed: {
    icon: CheckCircle2,
    className: "text-green-500 bg-green-500/10",
    labelKey: "completed",
  },
  Failed: {
    icon: XCircle,
    className: "text-red-500 bg-red-500/10",
    labelKey: "failed",
  },
};

function StatusBadge({ status }: { status: RepositoryStatus }) {
  const t = useTranslations();
  const config = STATUS_CONFIG[status];
  const Icon = config.icon;

  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium",
        config.className
      )}
    >
      <Icon
        className={cn("h-3.5 w-3.5", status === "Processing" && "animate-spin")}
      />
      {t(`home.repository.status.${config.labelKey}`)}
    </span>
  );
}

function getSourceTypeLabelKey(sourceType: RepositorySourceType) {
  switch (sourceType) {
    case "Archive":
      return "sourceTypeArchive";
    case "LocalDirectory":
      return "sourceTypeLocal";
    default:
      return "sourceTypeGit";
  }
}

function RepositoryCard({ 
  repo, 
  onVisibilityChange,
  onRetry,
  isRetrying,
}: { 
  repo: RepositoryItemResponse;
  onVisibilityChange: (repoId: string, newIsPublic: boolean) => void;
  onRetry: (repo: RepositoryItemResponse) => void;
  isRetrying: boolean;
}) {
  const t = useTranslations();
  const createdDate = new Date(repo.createdAt).toLocaleDateString();

  // 生成正确编码的Wiki导航URL
  // 使用encodeURIComponent处理特殊字符，确保URL安全
  const wikiUrl = buildRepoBasePath(repo.orgName, repo.repoName);

  const handleVisibilityChange = (newIsPublic: boolean) => {
    onVisibilityChange(repo.id, newIsPublic);
  };

  return (
    <Card className="transition-shadow hover:shadow-md">
      <CardContent className="p-4">
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2">
              <GitBranch className="h-4 w-4 text-muted-foreground shrink-0" />
              <h3 className="font-medium truncate">
                {repo.orgName}/{repo.repoName}
              </h3>
            </div>
            <div className="mt-2">
              <span className="inline-flex rounded-full bg-secondary px-2 py-1 text-xs text-muted-foreground">
                {t(`home.repository.${getSourceTypeLabelKey(repo.sourceType)}`)}
              </span>
            </div>
            <p className="mt-1 text-sm text-muted-foreground truncate">
              {repo.sourceLocation || repo.gitUrl}
            </p>
            <p className="mt-2 text-xs text-muted-foreground">
              {t("home.repository.createdAt")}: {createdDate}
            </p>
          </div>
          <div className="flex flex-col items-end gap-2 shrink-0">
            <StatusBadge status={repo.statusName} />
            <VisibilityToggle
              repositoryId={repo.id}
              isPublic={repo.isPublic}
              hasPassword={repo.hasPassword}
              onVisibilityChange={handleVisibilityChange}
            />
            {repo.statusName === "Completed" && (
              <Button variant="outline" size="sm" asChild>
                <Link href={wikiUrl}>
                  <ExternalLink className="mr-1.5 h-3.5 w-3.5" />
                  {t("home.repository.viewWiki")}
                </Link>
              </Button>
            )}
            {repo.statusName === "Failed" && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => onRetry(repo)}
                disabled={isRetrying}
              >
                {isRetrying ? (
                  <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />
                ) : (
                  <RefreshCw className="mr-1.5 h-3.5 w-3.5" />
                )}
                {isRetrying
                  ? (t("home.repository.status.regenerating") || t("home.repository.retry"))
                  : t("home.repository.retry")}
              </Button>
            )}
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function RepositoryListSkeleton() {
  return (
    <div className="space-y-4">
      {[1, 2, 3].map((i) => (
        <Card key={i}>
          <CardContent className="p-4">
            <div className="flex items-start justify-between gap-4">
              <div className="flex-1 space-y-2">
                <Skeleton className="h-5 w-48" />
                <Skeleton className="h-4 w-64" />
                <Skeleton className="h-3 w-32" />
              </div>
              <Skeleton className="h-6 w-20 rounded-full" />
            </div>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

export function RepositoryList({ ownerId, refreshTrigger }: RepositoryListProps) {
  const t = useTranslations();
  const [repositories, setRepositories] = useState<RepositoryItemResponse[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [retryingRepoId, setRetryingRepoId] = useState<string | null>(null);

  const loadRepositories = useCallback(async () => {
    try {
      setIsLoading(true);
      setError(null);
      const response = await fetchRepositoryList({ ownerId });
      setRepositories(response.items);
    } catch (err) {
      setError("Failed to load repositories");
      console.error("Failed to fetch repositories:", err);
    } finally {
      setIsLoading(false);
    }
  }, [ownerId]);

  // 处理可见性变化，更新本地状态
  const handleVisibilityChange = useCallback((repoId: string, newIsPublic: boolean) => {
    setRepositories((prev) =>
      prev.map((repo) =>
        repo.id === repoId ? { ...repo, isPublic: newIsPublic } : repo
      )
    );
  }, []);

  const handleRetry = useCallback(async (repo: RepositoryItemResponse) => {
    setRetryingRepoId(repo.id);
    try {
      const result = await regenerateRepository(repo.orgName, repo.repoName);
      if (!result.success) {
        toast.error(result.errorMessage || t("home.actions.actionError"));
        return;
      }

      setRepositories((prev) =>
        prev.map((item) =>
          item.id === repo.id ? { ...item, statusName: "Pending" } : item
        )
      );
      void loadRepositories();
    } catch (err) {
      console.error("Failed to regenerate repository:", err);
      toast.error(t("home.actions.actionError"));
    } finally {
      setRetryingRepoId((current) => (current === repo.id ? null : current));
    }
  }, [loadRepositories, t]);

  useEffect(() => {
    loadRepositories();
  }, [loadRepositories, refreshTrigger]);

  // Auto-refresh for pending/processing repositories
  useEffect(() => {
    const hasPendingOrProcessing = repositories.some(
      (r) => r.statusName === "Pending" || r.statusName === "Processing"
    );

    if (hasPendingOrProcessing) {
      const interval = setInterval(loadRepositories, 10000); // Refresh every 10 seconds
      return () => clearInterval(interval);
    }
  }, [repositories, loadRepositories]);

  if (isLoading && repositories.length === 0) {
    return (
      <Card className="w-full">
        <CardHeader>
          <CardTitle>{t("home.repository.listTitle")}</CardTitle>
        </CardHeader>
        <CardContent>
          <RepositoryListSkeleton />
        </CardContent>
      </Card>
    );
  }

  if (error) {
    return (
      <Card className="w-full">
        <CardHeader>
          <CardTitle>{t("home.repository.listTitle")}</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="flex flex-col items-center justify-center py-8 text-center">
            <XCircle className="h-12 w-12 text-destructive mb-4" />
            <p className="text-muted-foreground">{error}</p>
            <Button
              variant="outline"
              className="mt-4"
              onClick={loadRepositories}
            >
              <RefreshCw className="mr-2 h-4 w-4" />
              {t("home.repository.retry")}
            </Button>
          </div>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card className="w-full">
      <CardHeader>
        <div className="flex items-center justify-between">
          <CardTitle>{t("home.repository.listTitle")}</CardTitle>
          <Button
            variant="ghost"
            size="icon"
            onClick={loadRepositories}
            disabled={isLoading}
          >
            <RefreshCw
              className={cn("h-4 w-4", isLoading && "animate-spin")}
            />
          </Button>
        </div>
        {repositories.length > 0 && (
          <CardDescription>
            {repositories.length} {repositories.length === 1 ? "repository" : "repositories"}
          </CardDescription>
        )}
      </CardHeader>
      <CardContent>
        {repositories.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-8 text-center">
            <GitBranch className="h-12 w-12 text-muted-foreground mb-4" />
            <p className="text-muted-foreground">
              {t("home.repository.noRepositories")}
            </p>
          </div>
        ) : (
          <div className="space-y-4">
            {repositories.map((repo) => (
              <RepositoryCard 
                key={repo.id} 
                repo={repo} 
                onVisibilityChange={handleVisibilityChange}
                onRetry={handleRetry}
                isRetrying={retryingRepoId === repo.id}
              />
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
