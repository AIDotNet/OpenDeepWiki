"use client";

import { useEffect, useState, useCallback, useMemo, useRef } from "react";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import { useTranslations } from "@/hooks/use-translations";
import { fetchRepositoryList } from "@/lib/repository-api";
import { getMyDepartmentRepositories, restrictRepoInOrganization, unrestrictRepoInOrganization } from "@/lib/organization-api";
import type { DepartmentRepository } from "@/lib/organization-api";
import { PublicRepositoryCard } from "./public-repository-card";
import { LanguageTags } from "./language-tags";
import type { RepositoryItemResponse } from "@/types/repository";
import type { LanguageInfo } from "@/lib/recommendation-api";
import { GitBranch, XCircle, RefreshCw, Search, ChevronLeft, ChevronRight, Globe, Building2, User, Layers, Plus } from "lucide-react";
import { cn } from "@/lib/utils";
import { useAuth } from "@/contexts/auth-context";
import Link from "next/link";
import { RepositorySubmitForm } from "@/components/repo/repository-submit-form";
import {
  Dialog,
  DialogContent,
} from "@/components/ui/dialog";

/**
 * ADR: Unified Dashboard Repository Views
 *
 * Data Sources:
 * - Public repos: server-side paginated via fetchRepositoryList({ isPublic: true })
 * - Org (department) repos: client-side via getMyDepartmentRepositories()
 * - User-owned repos: server-side via fetchRepositoryList({ ownerId: user.id })
 *
 * Key Design Decisions:
 *
 * 1. DUAL DATA MODEL: Public API returns RepositoryItemResponse (no departmentName),
 *    while org API returns DepartmentRepository (no star/fork counts). We enrich
 *    public repos with departmentName from dept data for dual-icon display.
 *
 * 2. OWNERSHIP vs DEPARTMENT: GitHub App imports set OwnerUserId to the importing
 *    admin, making all org repos appear as "owned". The Mine view therefore
 *    subtracts dept repos by default to show only truly personal repos.
 *
 * 3. SUB-FILTERS: Each view has contextual sub-filters applied client-side:
 *    - Public: "publicOnly" fetches all + dept IDs, excludes overlap
 *    - Organization: "privateOnly" filters mapped dept repos by !isPublic
 *    - Mine: default excludes dept repos; "shared"/"all" include them
 *
 * 4. RACE CONDITION PROTECTION: loadIdRef counter ensures only the latest
 *    async request's results are applied. Stale responses are silently discarded.
 *
 * 5. STALE STATE CLEARING: effectiveView change clears repositories, languages,
 *    total, page, and subFilter to prevent flash of previous view's data.
 *
 * 6. PAGINATION: Public (default) uses server-side pagination (PAGE_SIZE=12).
 *    All other views and sub-filtered public use client-side pagination
 *    (fetch MAX_CLIENT_PAGE_SIZE=200, slice locally).
 */

export type RepositoryView = "all" | "public" | "organization" | "mine";

interface PublicRepositoryListProps {
  keyword: string;
  view?: RepositoryView;
  onViewChange?: (view: RepositoryView) => void;
  className?: string;
}

const PAGE_SIZE = 12;
const MAX_CLIENT_PAGE_SIZE = 200;

function mapDepartmentRepoToItem(repo: DepartmentRepository): RepositoryItemResponse {
  return {
    id: repo.repositoryId,
    orgName: repo.orgName,
    repoName: repo.repoName,
    gitUrl: repo.gitUrl || "",
    status: repo.status,
    statusName: (repo.statusName as RepositoryItemResponse["statusName"]) || "Pending",
    isPublic: repo.isPublic ?? false,
    createdAt: repo.createdAt || "",
    departmentName: repo.departmentName,
    primaryLanguage: repo.primaryLanguage,
    isRestricted: repo.isRestricted || false,
  };
}

function RepositoryGridSkeleton() {
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
      {[1, 2, 3, 4, 5, 6].map((i) => (
        <div key={i} className="p-4 border rounded-lg">
          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <Skeleton className="h-5 w-40" />
              <Skeleton className="h-6 w-20 rounded-full" />
            </div>
            <Skeleton className="h-4 w-32" />
          </div>
        </div>
      ))}
    </div>
  );
}

function computeLanguageStats(repos: RepositoryItemResponse[]): LanguageInfo[] {
  const counts = new Map<string, number>();
  for (const r of repos) {
    if (r.primaryLanguage) {
      counts.set(r.primaryLanguage, (counts.get(r.primaryLanguage) || 0) + 1);
    }
  }
  return Array.from(counts.entries())
    .map(([name, count]) => ({ name, count }))
    .sort((a, b) => b.count - a.count);
}

const GithubIcon = ({ className }: { className?: string }) => (
  <svg className={className} viewBox="0 0 24 24" fill="currentColor" width="16" height="16">
    <path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z" />
  </svg>
);

export function PublicRepositoryList({ keyword, view = "public", onViewChange, className }: PublicRepositoryListProps) {
  const t = useTranslations();
  const { user } = useAuth();
  const isAdmin = user?.roles?.includes("Admin") ?? false;
  const [repositories, setRepositories] = useState<RepositoryItemResponse[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedLanguage, setSelectedLanguage] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [viewLanguages, setViewLanguages] = useState<LanguageInfo[] | null>(null);
  const [subFilter, setSubFilter] = useState<string | null>(null);

  // Effective view: fall back to "public" if auth-required view is used without auth
  const effectiveView = useMemo(() => {
    if ((view === "organization" || view === "mine") && !user) {
      return "public";
    }
    return view;
  }, [view, user]);

  const totalPages = Math.ceil(total / PAGE_SIZE);

  // Race condition protection: only the latest request's results are applied
  const loadIdRef = useRef(0);

  const loadRepositories = useCallback(async () => {
    const loadId = ++loadIdRef.current;
    try {
      setIsLoading(true);
      setError(null);

      switch (effectiveView) {
        case "public": {
          // Fetch public repos + dept repos (for enrichment & sub-filter)
          const pubDeptRepos = user
            ? await getMyDepartmentRepositories().catch(() => [] as DepartmentRepository[])
            : [];
          if (loadId !== loadIdRef.current) return;

          // Build dept lookup for enrichment
          const pubDeptMap = new Map(pubDeptRepos.map(r => [r.repositoryId, r.departmentName]));

          // Helper: enrich public repos with departmentName from dept data
          const enrichPublicRepos = (items: RepositoryItemResponse[]) =>
            items.map(r => pubDeptMap.has(r.id) ? { ...r, departmentName: pubDeptMap.get(r.id) } : r);

          if (subFilter === "publicOnly" && user) {
            // Client-side: fetch all public, exclude org repos
            const pubAllResponse = await fetchRepositoryList({
              isPublic: true,
              sortBy: "status",
              keyword: keyword || undefined,
              pageSize: MAX_CLIENT_PAGE_SIZE,
            });
            if (loadId !== loadIdRef.current) return;
            let filtered = pubAllResponse.items.filter(r => !pubDeptMap.has(r.id));
            setViewLanguages(computeLanguageStats(filtered));
            if (selectedLanguage) {
              filtered = filtered.filter(r => r.primaryLanguage?.toLowerCase() === selectedLanguage.toLowerCase());
            }
            setTotal(filtered.length);
            const pubStart = (page - 1) * PAGE_SIZE;
            setRepositories(filtered.slice(pubStart, pubStart + PAGE_SIZE));
          } else {
            // Default: server-side paginated, enrich with dept info
            setViewLanguages(null);
            const response = await fetchRepositoryList({
              isPublic: true,
              sortBy: "status",
              keyword: keyword || undefined,
              language: selectedLanguage || undefined,
              page,
              pageSize: PAGE_SIZE,
            });
            if (loadId !== loadIdRef.current) return;
            setRepositories(enrichPublicRepos(response.items));
            setTotal(response.total);
          }
          break;
        }

        case "mine": {
          if (!user) break;
          const [mineResponse, mineDeptRepos] = await Promise.all([
            fetchRepositoryList({
              ownerId: user.id,
              sortBy: "status",
              keyword: keyword || undefined,
              pageSize: MAX_CLIENT_PAGE_SIZE,
            }),
            getMyDepartmentRepositories().catch(() => [] as DepartmentRepository[]),
          ]);

          if (loadId !== loadIdRef.current) return;

          // Base: all owned private repos (exclude public since user didn't create those)
          const mineDeptRepoIds = new Set(mineDeptRepos.map(r => r.repositoryId));
          let myRepos = mineResponse.items.filter(r => !r.isPublic);

          // Sub-filter: default excludes dept repos (org-imported repos belong to org, not user)
          if (subFilter === "shared") {
            myRepos = myRepos.filter(r => mineDeptRepoIds.has(r.id));
          } else if (subFilter !== "all") {
            // Default (null) and "notShared": exclude dept repos = truly personal only
            myRepos = myRepos.filter(r => !mineDeptRepoIds.has(r.id));
          }

          setViewLanguages(computeLanguageStats(myRepos));
          if (selectedLanguage) {
            myRepos = myRepos.filter(r => r.primaryLanguage === selectedLanguage);
          }
          const mineTotal = myRepos.length;
          const mineStart = (page - 1) * PAGE_SIZE;
          setTotal(mineTotal);
          setRepositories(myRepos.slice(mineStart, mineStart + PAGE_SIZE));
          break;
        }

        case "organization": {
          if (!user) break;
          const orgDeptRepos = await getMyDepartmentRepositories(isAdmin);
          if (loadId !== loadIdRef.current) return;
          let mapped = orgDeptRepos.map(mapDepartmentRepoToItem);

          setViewLanguages(computeLanguageStats(mapped));

          // Client-side keyword filter
          if (keyword) {
            const kw = keyword.toLowerCase();
            mapped = mapped.filter(
              (r) =>
                r.orgName.toLowerCase().includes(kw) ||
                r.repoName.toLowerCase().includes(kw)
            );
          }
          // Sub-filter: private only
          if (subFilter === "privateOnly") {
            mapped = mapped.filter((r) => !r.isPublic);
          }
          // Client-side language filter
          if (selectedLanguage) {
            mapped = mapped.filter(
              (r) => r.primaryLanguage?.toLowerCase() === selectedLanguage.toLowerCase()
            );
          }

          setTotal(mapped.length);
          // Client-side pagination
          const start = (page - 1) * PAGE_SIZE;
          setRepositories(mapped.slice(start, start + PAGE_SIZE));
          break;
        }

        case "all": {
          // For unauthenticated users, same as public
          if (!user) {
            setViewLanguages(null);
            const response = await fetchRepositoryList({
              isPublic: true,
              sortBy: "status",
              keyword: keyword || undefined,
              language: selectedLanguage || undefined,
              page,
              pageSize: PAGE_SIZE,
            });
            if (loadId !== loadIdRef.current) return;
            setRepositories(response.items);
            setTotal(response.total);
            break;
          }

          // Fetch all sources in parallel
          const [allPublicResponse, allDeptRepos, allOwnResponse] = await Promise.all([
            fetchRepositoryList({
              isPublic: true,
              sortBy: "status",
              keyword: keyword || undefined,
              pageSize: MAX_CLIENT_PAGE_SIZE,
            }),
            getMyDepartmentRepositories(isAdmin).catch(() => [] as DepartmentRepository[]),
            fetchRepositoryList({
              ownerId: user.id,
              sortBy: "status",
              keyword: keyword || undefined,
              pageSize: MAX_CLIENT_PAGE_SIZE,
            }).catch(() => ({ items: [] as RepositoryItemResponse[], total: 0 })),
          ]);

          if (loadId !== loadIdRef.current) return;

          // Merge: start with owned repos, then OVERWRITE with dept repos (they have departmentName), then add remaining public
          const repoMap = new Map<string, RepositoryItemResponse>();
          // First: owned repos (these may lack departmentName)
          if (user) {
            for (const r of allOwnResponse.items) repoMap.set(r.id, r);
          }
          // Second: dept repos OVERWRITE owned versions (to get correct departmentName + icon)
          for (const dr of allDeptRepos) {
            repoMap.set(dr.repositoryId, mapDepartmentRepoToItem(dr));
          }
          // Third: public repos (don't overwrite)
          for (const r of allPublicResponse.items) {
            if (!repoMap.has(r.id)) repoMap.set(r.id, r);
          }

          const allRepos = Array.from(repoMap.values());
          setViewLanguages(computeLanguageStats(allRepos));

          // Client-side language filter
          let filteredRepos = allRepos;
          if (selectedLanguage) {
            filteredRepos = allRepos.filter(
              (r) => r.primaryLanguage?.toLowerCase() === selectedLanguage.toLowerCase()
            );
          }
          setTotal(filteredRepos.length);
          // Client-side pagination
          const allStart = (page - 1) * PAGE_SIZE;
          setRepositories(filteredRepos.slice(allStart, allStart + PAGE_SIZE));
          break;
        }
      }
    } catch (err) {
      if (loadId !== loadIdRef.current) return;
      setError("Failed to load repositories");
      console.error("Failed to fetch repositories:", err);
    } finally {
      if (loadId === loadIdRef.current) {
        setIsLoading(false);
      }
    }
  }, [effectiveView, keyword, selectedLanguage, page, user, subFilter]);

  useEffect(() => {
    loadRepositories();
  }, [loadRepositories]);

  // Clear stale state when view changes (prevents flash of old data)
  useEffect(() => {
    setRepositories([]);
    setViewLanguages(null);
    setTotal(0);
    setPage(1);
    setSubFilter(null);
  }, [effectiveView]);

  // Reset page when keyword/language/sub filter changes (within same view)
  useEffect(() => {
    setPage(1);
  }, [keyword, selectedLanguage, subFilter]);

  // Auto-refresh for pending/processing repositories (ported from repository-list.tsx)
  useEffect(() => {
    if (effectiveView === "public") return; // Public repos don't need auto-refresh

    const hasPendingOrProcessing = repositories.some(
      (r) => r.statusName === "Pending" || r.statusName === "Processing"
    );

    if (hasPendingOrProcessing) {
      const interval = setInterval(loadRepositories, 10000);
      return () => clearInterval(interval);
    }
  }, [repositories, loadRepositories, effectiveView]);

  const handleLanguageChange = (language: string | null) => {
    setSelectedLanguage(language);
  };

  const handlePrevPage = () => {
    if (page > 1) setPage(page - 1);
  };

  const handleNextPage = () => {
    if (page < totalPages) setPage(page + 1);
  };

  const handleViewChange = (newView: RepositoryView) => {
    onViewChange?.(newView);
  };

  const handleSubmitSuccess = useCallback(() => {
    setIsFormOpen(false);
    loadRepositories();
  }, [loadRepositories]);

  // Dynamic section title
  const sectionTitle = useMemo(() => {
    switch (effectiveView) {
      case "all":
        return t("home.filter.allTitle");
      case "public":
        return t("home.publicRepository.title");
      case "organization":
        return t("home.filter.organizationTitle");
      case "mine":
        return t("home.repository.listTitle");
    }
  }, [effectiveView, t]);

  // Dynamic empty state message
  const emptyMessage = useMemo(() => {
    switch (effectiveView) {
      case "organization":
        return t("home.filter.organizationEmpty");
      case "mine":
        return t("home.filter.myReposEmpty");
      default:
        return t("home.publicRepository.empty");
    }
  }, [effectiveView, t]);

  // Filter tabs
  const filterTabs: { key: RepositoryView; label: string; icon: React.ElementType; requireAuth: boolean }[] = [
    { key: "all", label: t("home.filter.all"), icon: Layers, requireAuth: false },
    { key: "public", label: t("home.filter.public"), icon: Globe, requireAuth: false },
    { key: "organization", label: t("home.filter.organization"), icon: Building2, requireAuth: true },
    { key: "mine", label: t("home.filter.myRepos"), icon: User, requireAuth: true },
  ];

  const visibleTabs = filterTabs.filter((tab) => !tab.requireAuth || user);

  const subFilterOptions = useMemo(() => {
    switch (effectiveView) {
      case "public":
        return user ? [
          { key: null, label: t("home.filter.subAll") },
          { key: "publicOnly", label: t("home.filter.publicOnly") },
        ] : [];
      case "organization":
        return [
          { key: null, label: t("home.filter.subAll") },
          { key: "privateOnly", label: t("home.filter.privateOnly") },
        ];
      case "mine":
        return [
          { key: null, label: t("home.filter.notSharedOnly") },
          { key: "shared", label: t("home.filter.sharedOnly") },
          { key: "all", label: t("home.filter.subAll") },
        ];
      default:
        return [];
    }
  }, [effectiveView, user, t]);

  if (isLoading && repositories.length === 0) {
    return (
      <div className={cn("w-full", className)}>
        {/* Filter tabs */}
        <div className="flex flex-wrap items-center gap-2 mb-6">
          {visibleTabs.map((tab) => (
            <Button
              key={tab.key}
              variant={effectiveView === tab.key ? "default" : "outline"}
              size="sm"
              className="gap-1.5"
              onClick={() => handleViewChange(tab.key)}
            >
              <tab.icon className="h-3.5 w-3.5" />
              {tab.label}
            </Button>
          ))}
        </div>
        {/* Sub-filter chips */}
        {subFilterOptions.length > 0 && (
          <div className="flex flex-wrap items-center gap-1.5 mb-4">
            {subFilterOptions.map((sf) => (
              <Button
                key={sf.key ?? "all"}
                variant={subFilter === sf.key ? "secondary" : "ghost"}
                size="sm"
                className="h-7 text-xs"
                onClick={() => setSubFilter(sf.key)}
              >
                {sf.label}
              </Button>
            ))}
          </div>
        )}
        <h2 className="text-xl font-semibold mb-4">{sectionTitle}</h2>
        <div className="mb-6">
          <div className="flex flex-wrap gap-2">
            {[1, 2, 3, 4, 5, 6].map((i) => (
              <Skeleton key={i} className="h-7 w-20 rounded-full" />
            ))}
          </div>
        </div>
        <RepositoryGridSkeleton />
      </div>
    );
  }

  if (error) {
    return (
      <div className={cn("w-full", className)}>
        {/* Filter tabs */}
        <div className="flex flex-wrap items-center gap-2 mb-6">
          {visibleTabs.map((tab) => (
            <Button
              key={tab.key}
              variant={effectiveView === tab.key ? "default" : "outline"}
              size="sm"
              className="gap-1.5"
              onClick={() => handleViewChange(tab.key)}
            >
              <tab.icon className="h-3.5 w-3.5" />
              {tab.label}
            </Button>
          ))}
        </div>
        {/* Sub-filter chips */}
        {subFilterOptions.length > 0 && (
          <div className="flex flex-wrap items-center gap-1.5 mb-4">
            {subFilterOptions.map((sf) => (
              <Button
                key={sf.key ?? "all"}
                variant={subFilter === sf.key ? "secondary" : "ghost"}
                size="sm"
                className="h-7 text-xs"
                onClick={() => setSubFilter(sf.key)}
              >
                {sf.label}
              </Button>
            ))}
          </div>
        )}
        <h2 className="text-xl font-semibold mb-4">{sectionTitle}</h2>
        <div className="flex flex-col items-center justify-center py-12 text-center">
          <XCircle className="h-12 w-12 text-destructive mb-4" />
          <p className="text-muted-foreground mb-4">{t("home.publicRepository.loadError")}</p>
          <Button variant="outline" onClick={loadRepositories}>
            <RefreshCw className="mr-2 h-4 w-4" />
            {t("home.repository.retry")}
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className={cn("w-full", className)}>
      {/* Filter tabs */}
      <div className="flex flex-wrap items-center gap-2 mb-6">
        {visibleTabs.map((tab) => (
          <Button
            key={tab.key}
            variant={effectiveView === tab.key ? "default" : "outline"}
            size="sm"
            className="gap-1.5"
            onClick={() => handleViewChange(tab.key)}
          >
            <tab.icon className="h-3.5 w-3.5" />
            {tab.label}
          </Button>
        ))}

        {/* Action buttons for mine/organization views */}
        {user && (effectiveView === "mine" || effectiveView === "organization") && (
          <div className="ml-auto flex items-center gap-2">
            <Link href="/private/github-import">
              <Button variant="outline" size="sm" className="gap-1.5">
                <GithubIcon className="h-3.5 w-3.5" />
                {t("home.importFromGitHub")}
              </Button>
            </Link>
            <Dialog open={isFormOpen} onOpenChange={setIsFormOpen}>
              <Button
                size="sm"
                className="gap-1.5"
                onClick={() => setIsFormOpen(true)}
              >
                <Plus className="h-3.5 w-3.5" />
                {t("home.addPrivateRepo")}
              </Button>
              <DialogContent className="sm:max-w-md">
                <RepositorySubmitForm onSuccess={handleSubmitSuccess} />
              </DialogContent>
            </Dialog>
          </div>
        )}
      </div>

      {/* Sub-filter chips */}
      {subFilterOptions.length > 0 && (
        <div className="flex flex-wrap items-center gap-1.5 mb-4">
          {subFilterOptions.map((sf) => (
            <Button
              key={sf.key ?? "all"}
              variant={subFilter === sf.key ? "secondary" : "ghost"}
              size="sm"
              className="h-7 text-xs"
              onClick={() => setSubFilter(sf.key)}
            >
              {sf.label}
            </Button>
          ))}
        </div>
      )}

      <div className="flex items-center justify-between mb-4">
        <h2 className="text-xl font-semibold">{sectionTitle}</h2>
        <Button
          variant="ghost"
          size="icon"
          onClick={loadRepositories}
          disabled={isLoading}
        >
          <RefreshCw className={cn("h-4 w-4", isLoading && "animate-spin")} />
        </Button>
      </div>

      {/* Language tag filter */}
      <LanguageTags
        selectedLanguage={selectedLanguage}
        onLanguageChange={handleLanguageChange}
        className="mb-6"
        languages={viewLanguages ?? undefined}
      />

      {repositories.length === 0 && !keyword && !selectedLanguage ? (
        <div className="flex flex-col items-center justify-center py-12 text-center">
          <GitBranch className="h-12 w-12 text-muted-foreground mb-4" />
          <p className="text-muted-foreground">{emptyMessage}</p>
        </div>
      ) : repositories.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-12 text-center">
          <Search className="h-12 w-12 text-muted-foreground mb-4" />
          <p className="text-muted-foreground">
            {t("home.publicRepository.noResults")}
          </p>
        </div>
      ) : (
        <>
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {repositories.map((repo) => (
              <PublicRepositoryCard
                key={repo.id}
                repository={repo}
                {...(effectiveView === "mine"
                  ? { onShareToggle: () => { setSubFilter("all"); }, toggleMode: "share" as const }
                  : (effectiveView === "organization" && isAdmin)
                    ? { onShareToggle: () => loadRepositories(), toggleMode: "restrict" as const }
                    : {})}
              />
            ))}
          </div>

          {/* Pagination controls */}
          {totalPages > 1 && (
            <div className="flex items-center justify-center gap-4 mt-8">
              <Button
                variant="outline"
                size="sm"
                onClick={handlePrevPage}
                disabled={page === 1 || isLoading}
              >
                <ChevronLeft className="h-4 w-4 mr-1" />
                {t("home.bookmarks.previous")}
              </Button>
              <span className="text-sm text-muted-foreground">
                {t("home.bookmarks.pageInfo")
                  .replace("{current}", page.toString())
                  .replace("{total}", totalPages.toString())}
              </span>
              <Button
                variant="outline"
                size="sm"
                onClick={handleNextPage}
                disabled={page === totalPages || isLoading}
              >
                {t("home.bookmarks.next")}
                <ChevronRight className="h-4 w-4 ml-1" />
              </Button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
