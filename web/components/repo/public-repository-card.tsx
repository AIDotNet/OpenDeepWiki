"use client";

import { useState, useEffect, useCallback } from "react";
import Link from "next/link";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { useTranslations } from "@/hooks/use-translations";
import { useAuth } from "@/contexts/auth-context";
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
  GitBranch,
  Calendar,
  Bookmark,
  Bell,
  Star,
  GitFork,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { buildRepoBasePath } from "@/lib/repo-route";
import { addBookmark, removeBookmark, getBookmarkStatus } from "@/lib/bookmark-api";
import { addSubscription, removeSubscription, getSubscriptionStatus } from "@/lib/subscription-api";
import { toast } from "sonner";

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


interface PublicRepositoryCardProps {
  repository: RepositoryItemResponse;
}

export function PublicRepositoryCard({ repository }: PublicRepositoryCardProps) {
  const t = useTranslations();
  const { user } = useAuth();
  const createdDate = new Date(repository.createdAt).toLocaleDateString();
  const wikiUrl = buildRepoBasePath(repository.orgName, repository.repoName);

  const [isBookmarked, setIsBookmarked] = useState(false);
  const [isSubscribed, setIsSubscribed] = useState(false);
  const [bookmarkLoading, setBookmarkLoading] = useState(false);
  const [subscribeLoading, setSubscribeLoading] = useState(false);

  // 获取收藏和订阅状态
  useEffect(() => {
    if (!user) return;

    const fetchStatus = async () => {
      try {
        const [bookmarkRes, subscribeRes] = await Promise.all([
          getBookmarkStatus(repository.id, user.id),
          getSubscriptionStatus(repository.id, user.id),
        ]);
        setIsBookmarked(bookmarkRes.isBookmarked);
        setIsSubscribed(subscribeRes.isSubscribed);
      } catch {
        // 静默处理错误
      }
    };

    fetchStatus();
  }, [user, repository.id]);

  const handleBookmark = useCallback(async (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    if (!user || bookmarkLoading) return;

    setBookmarkLoading(true);
    try {
      if (isBookmarked) {
        await removeBookmark(repository.id, user.id);
        setIsBookmarked(false);
        toast.success(t("home.actions.bookmarkRemoved"));
      } else {
        await addBookmark({ userId: user.id, repositoryId: repository.id });
        setIsBookmarked(true);
        toast.success(t("home.actions.bookmarkSuccess"));
      }
    } catch {
      toast.error(t("home.actions.actionError"));
    } finally {
      setBookmarkLoading(false);
    }
  }, [user, repository.id, isBookmarked, bookmarkLoading, t]);

  const handleSubscribe = useCallback(async (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    if (!user || subscribeLoading) return;

    setSubscribeLoading(true);
    try {
      if (isSubscribed) {
        await removeSubscription(repository.id, user.id);
        setIsSubscribed(false);
        toast.success(t("home.actions.subscribeRemoved"));
      } else {
        await addSubscription({ userId: user.id, repositoryId: repository.id });
        setIsSubscribed(true);
        toast.success(t("home.actions.subscribeSuccess"));
      }
    } catch {
      toast.error(t("home.actions.actionError"));
    } finally {
      setSubscribeLoading(false);
    }
  }, [user, repository.id, isSubscribed, subscribeLoading, t]);

  return (
    <Link href={wikiUrl}>
      <Card className="h-full transition-all hover:shadow-md hover:border-primary/50 cursor-pointer">
        <CardContent className="p-4">
          <div className="flex flex-col gap-3">
            <div className="flex items-center justify-between gap-2">
              <div className="flex items-center gap-2 min-w-0">
                <GitBranch className="h-4 w-4 text-muted-foreground shrink-0" />
                <h3 className="font-medium truncate">
                  {repository.orgName}/{repository.repoName}
                </h3>
              </div>
              <StatusBadge status={repository.statusName} />
            </div>
            <div className="flex items-center justify-between gap-2">
              <span className="inline-flex rounded-full bg-secondary px-2 py-1 text-xs text-muted-foreground">
                {t(`home.repository.${getSourceTypeLabelKey(repository.sourceType)}`)}
              </span>
              <p className="truncate text-xs text-muted-foreground">
                {repository.sourceLocation || repository.gitUrl}
              </p>
            </div>
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-3 text-xs text-muted-foreground">
                <div className="flex items-center gap-1">
                  <Calendar className="h-3.5 w-3.5" />
                  <span>{createdDate}</span>
                </div>
                {typeof repository.starCount === "number" && (
                  <div className="flex items-center gap-1">
                    <Star className="h-3.5 w-3.5" />
                    <span>{repository.starCount.toLocaleString()}</span>
                  </div>
                )}
                {typeof repository.forkCount === "number" && (
                  <div className="flex items-center gap-1">
                    <GitFork className="h-3.5 w-3.5" />
                    <span>{repository.forkCount.toLocaleString()}</span>
                  </div>
                )}
              </div>
              {/* 收藏和订阅按钮 - 仅登录用户可见 */}
              {user && (
                <div className="flex items-center gap-1">
                  <Button
                    variant="ghost"
                    size="icon"
                    className={cn(
                      "h-7 w-7",
                      isBookmarked && "text-yellow-500 hover:text-yellow-600"
                    )}
                    onClick={handleBookmark}
                    disabled={bookmarkLoading}
                    title={isBookmarked ? t("home.actions.bookmarked") : t("home.actions.bookmark")}
                  >
                    {bookmarkLoading ? (
                      <Loader2 className="h-4 w-4 animate-spin" />
                    ) : (
                      <Bookmark className={cn("h-4 w-4", isBookmarked && "fill-current")} />
                    )}
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    className={cn(
                      "h-7 w-7",
                      isSubscribed && "text-blue-500 hover:text-blue-600"
                    )}
                    onClick={handleSubscribe}
                    disabled={subscribeLoading}
                    title={isSubscribed ? t("home.actions.subscribed") : t("home.actions.subscribe")}
                  >
                    {subscribeLoading ? (
                      <Loader2 className="h-4 w-4 animate-spin" />
                    ) : (
                      <Bell className={cn("h-4 w-4", isSubscribed && "fill-current")} />
                    )}
                  </Button>
                </div>
              )}
            </div>
          </div>
        </CardContent>
      </Card>
    </Link>
  );
}
