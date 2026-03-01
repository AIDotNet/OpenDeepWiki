"use client";

import { useState, useEffect, useCallback } from "react";
import { AppLayout } from "@/components/app-layout";
import { useTranslations } from "@/hooks/use-translations";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Bell, BellOff, Star, GitFork, Loader2 } from "lucide-react";
import { useAuth } from "@/contexts/auth-context";
import { getUserSubscriptions, removeSubscription, SubscriptionItemResponse } from "@/lib/subscription-api";

export default function SubscribePage() {
  const t = useTranslations();
  const { user, isAuthenticated, isLoading: authLoading } = useAuth();
  const [activeItem, setActiveItem] = useState(t("sidebar.subscribe"));
  const [subscriptions, setSubscriptions] = useState<SubscriptionItemResponse[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [removingId, setRemovingId] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const pageSize = 12;

  const fetchSubscriptions = useCallback(async () => {
    if (!user?.id) return;

    setIsLoading(true);
    setError(null);
    try {
      const response = await getUserSubscriptions(user.id, page, pageSize);
      setSubscriptions(response.items);
      setTotal(response.total);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("subscribe.failedToLoad"));
    } finally {
      setIsLoading(false);
    }
  }, [user?.id, page]);

  useEffect(() => {
    if (!authLoading && isAuthenticated) {
      fetchSubscriptions();
    } else if (!authLoading && !isAuthenticated) {
      setIsLoading(false);
    }
  }, [authLoading, isAuthenticated, fetchSubscriptions]);

  const handleUnsubscribe = async (repositoryId: string) => {
    if (!user?.id) return;

    setRemovingId(repositoryId);
    try {
      const response = await removeSubscription(repositoryId, user.id);
      if (response.success) {
        setSubscriptions(prev => prev.filter(s => s.repositoryId !== repositoryId));
        setTotal(prev => prev - 1);
      } else {
        setError(response.errorMessage || t("subscribe.failedToUnsubscribe"));
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : t("subscribe.failedToUnsubscribe"));
    } finally {
      setRemovingId(null);
    }
  };

  const formatDate = (dateStr: string) => {
    const date = new Date(dateStr);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24));

    if (diffDays === 0) return t("subscribe.today");
    if (diffDays === 1) return t("subscribe.yesterday");
    if (diffDays < 7) return t("subscribe.daysAgo", { count: diffDays });
    if (diffDays < 30) return t("subscribe.weeksAgo", { count: Math.floor(diffDays / 7) });
    return t("subscribe.monthsAgo", { count: Math.floor(diffDays / 30) });
  };

  const formatNumber = (num: number) => {
    if (num >= 1000) return `${(num / 1000).toFixed(1)}k`;
    return num.toString();
  };

  // Show login prompt if not authenticated
  if (!authLoading && !isAuthenticated) {
    return (
      <AppLayout activeItem={activeItem} onItemClick={setActiveItem}>
        <div className="flex flex-1 flex-col items-center justify-center gap-4 p-4 md:p-6">
          <Bell className="h-16 w-16 text-muted-foreground" />
          <h2 className="text-xl font-semibold">{t("subscribe.loginRequired")}</h2>
          <p className="text-muted-foreground">{t("subscribe.loginPrompt")}</p>
        </div>
      </AppLayout>
    );
  }

  return (
    <AppLayout activeItem={activeItem} onItemClick={setActiveItem}>
      <div className="flex flex-1 flex-col gap-4 p-4 md:p-6">
        <div className="space-y-2">
          <h1 className="text-3xl font-bold tracking-tight">{t("sidebar.subscribe")}</h1>
          <p className="text-muted-foreground">
            {t("subscribe.description")}
          </p>
        </div>

        {error && (
          <div className="bg-destructive/10 text-destructive px-4 py-2 rounded-md">
            {error}
          </div>
        )}

        {isLoading || authLoading ? (
          <div className="flex items-center justify-center py-12">
            <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
          </div>
        ) : subscriptions.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-12 gap-4">
            <Bell className="h-16 w-16 text-muted-foreground" />
            <h2 className="text-xl font-semibold">{t("subscribe.noSubscriptions")}</h2>
            <p className="text-muted-foreground">{t("subscribe.noSubscriptionsPrompt")}</p>
          </div>
        ) : (
          <>
            <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
              {subscriptions.map((repo) => (
                <Card key={repo.subscriptionId} className="hover:shadow-lg transition-shadow">
                  <CardHeader>
                    <div className="flex items-start justify-between">
                      <div className="flex-1">
                        <div className="flex items-center gap-2">
                          <Bell className="h-4 w-4 text-blue-500" />
                          <CardTitle className="text-lg">{repo.repoName}</CardTitle>
                        </div>
                        <CardDescription className="text-sm text-muted-foreground mt-1">
                          {repo.orgName}
                        </CardDescription>
                      </div>
                      <Button
                        variant="default"
                        size="sm"
                        className="gap-1"
                        onClick={() => handleUnsubscribe(repo.repositoryId)}
                        disabled={removingId === repo.repositoryId}
                      >
                        {removingId === repo.repositoryId ? (
                          <Loader2 className="h-3 w-3 animate-spin" />
                        ) : (
                          <>
                            <BellOff className="h-3 w-3" />
                            {t("subscribe.unwatch")}
                          </>
                        )}
                      </Button>
                    </div>
                  </CardHeader>
                  <CardContent>
                    <p className="text-sm mb-3 line-clamp-2">{repo.description || t("subscribe.noDescription")}</p>
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-3 text-sm text-muted-foreground">
                        <div className="flex items-center gap-1">
                          <Star className="h-3 w-3" />
                          <span>{formatNumber(repo.starCount)}</span>
                        </div>
                        <div className="flex items-center gap-1">
                          <GitFork className="h-3 w-3" />
                          <span>{formatNumber(repo.forkCount)}</span>
                        </div>
                      </div>
                      <span className="text-xs text-muted-foreground">{formatDate(repo.subscribedAt)}</span>
                    </div>
                  </CardContent>
                </Card>
              ))}
            </div>

            {/* Pagination */}
            {total > pageSize && (
              <div className="flex items-center justify-center gap-2 mt-4">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setPage(p => Math.max(1, p - 1))}
                  disabled={page === 1}
                >
                  {t("subscribe.previous")}
                </Button>
                <span className="text-sm text-muted-foreground">
                  {t("subscribe.pageOf", { current: page, total: Math.ceil(total / pageSize) })}
                </span>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setPage(p => p + 1)}
                  disabled={page >= Math.ceil(total / pageSize)}
                >
                  {t("subscribe.next")}
                </Button>
              </div>
            )}
          </>
        )}
      </div>
    </AppLayout>
  );
}
