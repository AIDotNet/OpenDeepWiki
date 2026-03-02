"use client";

import { useState, useEffect, useCallback } from "react";
import Link from "next/link";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { useTranslations } from "@/hooks/use-translations";
import { useAuth } from "@/contexts/auth-context";
import type { RepositoryItemResponse, RepositoryStatus } from "@/types/repository";
import {
  Clock,
  Loader2,
  CheckCircle2,
  XCircle,
  Globe,
  Building2,
  Calendar,
  Bookmark,
  Bell,
  Star,
  GitFork,
  Lock,
  EyeOff,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { addBookmark, removeBookmark, getBookmarkStatus } from "@/lib/bookmark-api";
import { addSubscription, removeSubscription, getSubscriptionStatus } from "@/lib/subscription-api";
import { shareRepoWithOrganization, unshareRepoFromOrganization, restrictRepoInOrganization, unrestrictRepoInOrganization } from "@/lib/organization-api";
import { Switch } from "@/components/ui/switch";
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


interface PublicRepositoryCardProps {
  repository: RepositoryItemResponse;
  onShareToggle?: (repositoryId: string, shared: boolean) => void;
  toggleMode?: "share" | "restrict";
}

export function PublicRepositoryCard({ repository, onShareToggle, toggleMode = "share" }: PublicRepositoryCardProps) {
  const t = useTranslations();
  const { user } = useAuth();
  const createdDate = repository.createdAt
    ? new Date(repository.createdAt).toLocaleDateString()
    : null;

  const [isBookmarked, setIsBookmarked] = useState(false);
  const [isSubscribed, setIsSubscribed] = useState(false);
  const [bookmarkLoading, setBookmarkLoading] = useState(false);
  const [subscribeLoading, setSubscribeLoading] = useState(false);
  const [shareLoading, setShareLoading] = useState(false);

  // Fetch bookmark and subscription status
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
        // Silently handle errors
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

  const handleShareToggle = useCallback(async (checked: boolean) => {
    if (shareLoading) return;
    setShareLoading(true);
    try {
      if (toggleMode === "restrict") {
        // Admin restrict/unrestrict in org view
        if (checked) {
          // Toggle ON = visible = unrestrict
          await unrestrictRepoInOrganization(repository.id);
          toast.success(t("home.filter.visibleToOrg"));
        } else {
          // Toggle OFF = restricted
          await restrictRepoInOrganization(repository.id);
          toast.success(t("home.filter.restricted"));
        }
      } else {
        // User share/unshare in mine view
        if (checked) {
          await shareRepoWithOrganization(repository.id);
          toast.success(t("home.filter.sharedWithOrg"));
        } else {
          await unshareRepoFromOrganization(repository.id);
          toast.success(t("home.filter.unsharedFromOrg"));
        }
      }
      onShareToggle?.(repository.id, checked);
    } catch {
      toast.error(t("home.actions.actionError"));
    } finally {
      setShareLoading(false);
    }
  }, [repository.id, shareLoading, onShareToggle, toggleMode, t]);

  return (
    <Link href={`/${repository.orgName}/${repository.repoName}`}>
      <Card className={cn(
        "h-full transition-all hover:shadow-md hover:border-primary/50 cursor-pointer",
        repository.isRestricted && "opacity-60"
      )}>
        <CardContent className="p-4">
          <div className="flex flex-col gap-3">
            <div className="flex items-center justify-between gap-2">
              <div className="flex items-center gap-2 min-w-0">
                {/* Icons: dual display for repos that are both public AND in a department.
                    Restricted repos show EyeOff regardless. Private-only shows Lock. */}
                {repository.isRestricted ? (
                  <span title={t("home.icons.restricted")}><EyeOff className="h-4 w-4 text-gray-400 shrink-0" /></span>
                ) : (
                  <div className="flex items-center gap-0.5 shrink-0">
                    {repository.isPublic && (
                      <span title={t("home.icons.public")}><Globe className="h-4 w-4 text-green-500" /></span>
                    )}
                    {repository.departmentName && (
                      <span title={t("home.icons.organization")}><Building2 className="h-4 w-4 text-blue-500" /></span>
                    )}
                    {!repository.isPublic && !repository.departmentName && (
                      <span title={t("home.icons.private")}><Lock className="h-4 w-4 text-amber-500" /></span>
                    )}
                  </div>
                )}
                <h3 className="font-medium truncate">
                  {repository.orgName}/{repository.repoName}
                </h3>
              </div>
              <StatusBadge status={repository.statusName} />
            </div>
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-3 text-xs text-muted-foreground">
                {createdDate && createdDate !== "Invalid Date" && (
                  <div className="flex items-center gap-1">
                    <Calendar className="h-3.5 w-3.5" />
                    <span>{createdDate}</span>
                  </div>
                )}
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
              {/* Bookmark and subscribe buttons - visible to logged-in users only */}
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
          {onShareToggle && (
            <div className="flex items-center gap-2 pt-2 border-t mt-2" onClick={(e) => { e.preventDefault(); e.stopPropagation(); }}>
              {shareLoading ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                <Building2 className="h-4 w-4 text-muted-foreground" />
              )}
              <Switch
                checked={toggleMode === "restrict" ? !repository.isRestricted : !!repository.departmentName}
                onCheckedChange={handleShareToggle}
                disabled={shareLoading}
              />
              <span className="text-sm text-muted-foreground">
                {toggleMode === "restrict"
                  ? (repository.isRestricted ? t("home.filter.restricted") : t("home.filter.visibleToOrg"))
                  : (repository.departmentName ? t("home.filter.sharedWithOrg") : t("home.filter.shareWithOrg"))}
              </span>
            </div>
          )}
        </CardContent>
      </Card>
    </Link>
  );
}
