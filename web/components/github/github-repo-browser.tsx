"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useTranslations } from "@/hooks/use-translations";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Input } from "@/components/ui/input";
import {
  ExternalLink,
  GitBranch,
  Star,
  GitFork,
  Lock,
  Globe,
  Loader2,
  CheckCircle2,
  Download,
  Search,
} from "lucide-react";

import type { GitHubInstallation, GitHubRepo, BatchImportResult } from "@/lib/admin-api";

export type { GitHubInstallation, GitHubRepo, BatchImportResult };

export interface GitHubRepoList {
  totalCount: number;
  repositories: GitHubRepo[];
  page: number;
  perPage: number;
}

export interface ImportRepoData {
  fullName: string;
  name: string;
  owner: string;
  cloneUrl: string;
  defaultBranch: string;
  private: boolean;
  language?: string;
  stargazersCount: number;
  forksCount: number;
}

interface GitHubRepoBrowserProps {
  installation: GitHubInstallation;
  /** Function to fetch repos from the API (admin or user endpoint) */
  fetchRepos: (installationId: number, page: number, perPage: number) => Promise<GitHubRepoList>;
  /** Available departments for the import selector */
  departments: Array<{ id: string; name: string }>;
  /** Handler for import action */
  onImport: (params: {
    installationId: number;
    departmentId: string;
    languageCode: string;
    repos: ImportRepoData[];
  }) => Promise<BatchImportResult>;
  /** Whether to show a "Personal only" option in the department selector */
  showPersonalOption?: boolean;
}

const PAGE_SIZE = 30;
const FETCH_BATCH_SIZE = 100;
const PERSONAL_ONLY_VALUE = "__personal__";

export function GitHubRepoBrowser({
  installation,
  fetchRepos,
  departments,
  onImport,
  showPersonalOption = false,
}: GitHubRepoBrowserProps) {
  const t = useTranslations();

  // All repos (fetched in full)
  const [allRepos, setAllRepos] = useState<GitHubRepo[]>([]);
  const [repoTotalCount, setRepoTotalCount] = useState(0);
  const [repoLoading, setRepoLoading] = useState(false);
  const [loadProgress, setLoadProgress] = useState("");

  // Selection
  const [selectedRepos, setSelectedRepos] = useState<Set<string>>(new Set());

  // Filters
  const [searchQuery, setSearchQuery] = useState("");
  const [languageFilter, setLanguageFilter] = useState<string>("all");
  const [importStatusFilter, setImportStatusFilter] = useState<"all" | "not_imported" | "imported">("all");

  // Gmail-style select scope
  const [selectAllScope, setSelectAllScope] = useState<"page" | "all">("page");

  // Client-side pagination
  const [page, setPage] = useState(1);

  // Import
  const [selectedDepartmentId, setSelectedDepartmentId] = useState<string>(PERSONAL_ONLY_VALUE);
  const [languageCode, setLanguageCode] = useState("en");
  const [importing, setImporting] = useState(false);
  const [importResult, setImportResult] = useState<BatchImportResult | null>(null);

  // Set default department when departments change
  useEffect(() => {
    if (showPersonalOption) {
      // For user mode, default to "Personal only"
      if (selectedDepartmentId === PERSONAL_ONLY_VALUE && departments.length > 0) {
        // Keep personal as default
      }
    } else {
      // For admin mode, default to first department
      if (departments.length > 0 && selectedDepartmentId === PERSONAL_ONLY_VALUE) {
        setSelectedDepartmentId(departments[0].id);
      }
    }
  }, [departments, showPersonalOption, selectedDepartmentId]);

  // Fetch ALL repos from the installation (paginated API calls in background)
  const fetchAllRepos = useCallback(async () => {
    setRepoLoading(true);
    setAllRepos([]);
    setRepoTotalCount(0);
    setLoadProgress("");

    try {
      // First request to get total count
      const firstResult = await fetchRepos(
        installation.installationId,
        1,
        FETCH_BATCH_SIZE
      );
      const total = firstResult.totalCount;
      setRepoTotalCount(total);

      let accumulated = [...firstResult.repositories];
      setAllRepos(accumulated);
      setLoadProgress(`${accumulated.length} / ${total}`);

      // Fetch remaining pages
      const totalPages = Math.ceil(total / FETCH_BATCH_SIZE);
      for (let p = 2; p <= totalPages; p++) {
        const result = await fetchRepos(
          installation.installationId,
          p,
          FETCH_BATCH_SIZE
        );
        accumulated = [...accumulated, ...result.repositories];
        setAllRepos(accumulated);
        setLoadProgress(`${accumulated.length} / ${total}`);
      }
    } catch (error) {
      toast.error(t("admin.githubImport.fetchReposFailed"));
    } finally {
      setRepoLoading(false);
      setLoadProgress("");
    }
  }, [installation.installationId, fetchRepos, t]);

  // Fetch repos when installation changes
  useEffect(() => {
    setSelectedRepos(new Set());
    setImportResult(null);
    setSearchQuery("");
    setLanguageFilter("all");
    setImportStatusFilter("all");
    setSelectAllScope("page");
    setPage(1);
    fetchAllRepos();
  }, [installation.installationId]);

  const toggleRepo = (fullName: string) => {
    setSelectAllScope("page");
    setSelectedRepos((prev) => {
      const next = new Set(prev);
      if (next.has(fullName)) {
        next.delete(fullName);
      } else {
        next.add(fullName);
      }
      return next;
    });
  };

  // Apply filters across ALL repos
  const filteredRepos = useMemo(() => {
    return allRepos.filter((r) => {
      if (searchQuery) {
        const q = searchQuery.toLowerCase();
        const matchesName = r.fullName.toLowerCase().includes(q);
        const matchesDesc = r.description?.toLowerCase().includes(q);
        if (!matchesName && !matchesDesc) return false;
      }
      if (languageFilter !== "all" && (r.language || "").toLowerCase() !== languageFilter.toLowerCase()) {
        return false;
      }
      if (importStatusFilter === "not_imported" && r.alreadyImported) return false;
      if (importStatusFilter === "imported" && !r.alreadyImported) return false;
      return true;
    });
  }, [allRepos, searchQuery, languageFilter, importStatusFilter]);

  const importableFiltered = useMemo(
    () => filteredRepos.filter((r) => !r.alreadyImported),
    [filteredRepos]
  );

  // Client-side pagination of filtered results
  const totalFilteredPages = Math.ceil(filteredRepos.length / PAGE_SIZE);
  const pagedRepos = useMemo(
    () => filteredRepos.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE),
    [filteredRepos, page]
  );

  // Collect unique languages from ALL repos for the filter dropdown
  const repoLanguages = useMemo(
    () => Array.from(new Set(allRepos.map((r) => r.language).filter(Boolean) as string[])).sort(),
    [allRepos]
  );

  // Reset page and selection scope when filters change
  useEffect(() => {
    setPage(1);
    setSelectAllScope("page");
  }, [searchQuery, languageFilter, importStatusFilter]);

  // Page-level selection helpers
  const importableOnPage = useMemo(
    () => pagedRepos.filter((r) => !r.alreadyImported),
    [pagedRepos]
  );
  const allPageSelected = importableOnPage.length > 0 && importableOnPage.every((r) => selectedRepos.has(r.fullName));
  const somePageSelected = importableOnPage.some((r) => selectedRepos.has(r.fullName));
  const hasMoreBeyondPage = importableFiltered.length > importableOnPage.length;

  const toggleSelectAll = () => {
    if (allPageSelected) {
      setSelectedRepos(new Set());
      setSelectAllScope("page");
    } else {
      const pageNames = new Set(importableOnPage.map((r) => r.fullName));
      setSelectedRepos((prev) => {
        const next = new Set(prev);
        pageNames.forEach((name) => next.add(name));
        return next;
      });
      setSelectAllScope("page");
    }
  };

  const handleSelectAllMatching = () => {
    setSelectedRepos(new Set(importableFiltered.map((r) => r.fullName)));
    setSelectAllScope("all");
  };

  const handleClearSelection = () => {
    setSelectedRepos(new Set());
    setSelectAllScope("page");
  };

  // Determine if import button should be enabled
  // When showPersonalOption is true, PERSONAL_ONLY_VALUE (personal) is valid
  const isDepartmentValid = showPersonalOption
    ? true
    : selectedDepartmentId !== PERSONAL_ONLY_VALUE;

  const handleImport = async () => {
    if (selectedRepos.size === 0 || !isDepartmentValid) return;

    setImporting(true);
    setImportResult(null);
    try {
      const selectedRepoData: ImportRepoData[] = allRepos
        .filter((r) => selectedRepos.has(r.fullName))
        .map((r) => ({
          fullName: r.fullName,
          name: r.name,
          owner: r.owner,
          cloneUrl: r.cloneUrl,
          defaultBranch: r.defaultBranch,
          private: r.private,
          language: r.language,
          stargazersCount: r.stargazersCount,
          forksCount: r.forksCount,
        }));

      const result = await onImport({
        installationId: installation.installationId,
        departmentId: selectedDepartmentId === PERSONAL_ONLY_VALUE ? "" : selectedDepartmentId,
        languageCode,
        repos: selectedRepoData,
      });

      setImportResult(result);
      setSelectedRepos(new Set());
      toast.success(
        t("admin.githubImport.importSuccess")
          .replace("{imported}", result.imported.toString())
          .replace("{skipped}", result.skipped.toString())
      );

      // Refresh repo list to update "already imported" flags
      fetchAllRepos();
    } catch (error) {
      toast.error(
        error instanceof Error ? error.message : t("admin.githubImport.importFailed")
      );
    } finally {
      setImporting(false);
    }
  };

  return (
    <div className="space-y-4">
      {/* Card Header Info */}
      <div>
        <h3 className="text-lg font-semibold">
          {t("admin.githubImport.importFrom").replace(
            "{org}",
            installation.accountLogin
          )}
        </h3>
        <p className="text-sm text-muted-foreground">
          {repoLoading && loadProgress
            ? `${t("admin.githubImport.loadingRepos")} ${loadProgress}...`
            : t("admin.githubImport.totalRepos").replace(
                "{count}",
                repoTotalCount.toString()
              )}
        </p>
      </div>

      {/* Import Options */}
      <div className="flex items-center gap-4 flex-wrap">
        <div className="flex items-center gap-2">
          <label className="text-sm font-medium">
            {t("admin.githubImport.department")}:
          </label>
          <Select
            value={selectedDepartmentId}
            onValueChange={setSelectedDepartmentId}
          >
            <SelectTrigger className="w-[200px]">
              <SelectValue placeholder={t("admin.githubImport.selectDepartment")} />
            </SelectTrigger>
            <SelectContent>
              {showPersonalOption && (
                <SelectItem value={PERSONAL_ONLY_VALUE}>
                  {t("home.githubImport.personalOnly")}
                </SelectItem>
              )}
              {departments.map((dept) => (
                <SelectItem key={dept.id} value={dept.id}>
                  {dept.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="flex items-center gap-2">
          <label className="text-sm font-medium">
            {t("admin.githubImport.language")}:
          </label>
          <Select value={languageCode} onValueChange={setLanguageCode}>
            <SelectTrigger className="w-[120px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="en">English</SelectItem>
              <SelectItem value="zh">Chinese</SelectItem>
              <SelectItem value="ko">Korean</SelectItem>
              <SelectItem value="ja">Japanese</SelectItem>
            </SelectContent>
          </Select>
        </div>
      </div>

      {/* Search and Filter */}
      <div className="flex items-center gap-3 flex-wrap">
        <div className="relative flex-1 min-w-[200px]">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
          <Input
            placeholder={t("admin.githubImport.searchPlaceholder")}
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="pl-9"
          />
        </div>
        <Select value={languageFilter} onValueChange={setLanguageFilter}>
          <SelectTrigger className="w-[160px]">
            <SelectValue placeholder={t("admin.githubImport.allLanguages")} />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t("admin.githubImport.allLanguages")}</SelectItem>
            {repoLanguages.map((lang) => (
              <SelectItem key={lang} value={lang.toLowerCase()}>
                {lang}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={importStatusFilter} onValueChange={(v) => setImportStatusFilter(v as "all" | "not_imported" | "imported")}>
          <SelectTrigger className="w-[160px]">
            <SelectValue placeholder={t("admin.githubImport.filterAll")} />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t("admin.githubImport.filterAll")}</SelectItem>
            <SelectItem value="not_imported">{t("admin.githubImport.filterNotImported")}</SelectItem>
            <SelectItem value="imported">{t("admin.githubImport.filterImported")}</SelectItem>
          </SelectContent>
        </Select>
      </div>

      {/* Select All */}
      <div className="border-b pb-2 space-y-0">
        <div className="flex items-center gap-2">
          <Checkbox
            checked={allPageSelected ? true : somePageSelected ? "indeterminate" : false}
            onCheckedChange={toggleSelectAll}
          />
          <span className="text-sm font-medium">
            {t("admin.githubImport.selectAll")}
            {selectedRepos.size > 0 &&
              ` - ${selectedRepos.size} ${t("admin.githubImport.selected")}`}
          </span>
        </div>
        {/* Gmail-style banner */}
        {allPageSelected && hasMoreBeyondPage && selectAllScope === "page" && (
          <div className="mt-1 py-1.5 px-3 bg-blue-50 dark:bg-blue-950 text-sm text-center rounded">
            <span className="text-blue-800 dark:text-blue-200">
              {t("admin.githubImport.allPageSelected").replace("{count}", importableOnPage.length.toString())}
            </span>{" "}
            <button
              type="button"
              className="text-blue-600 dark:text-blue-400 font-medium hover:underline"
              onClick={handleSelectAllMatching}
            >
              {t("admin.githubImport.selectAllMatching").replace("{count}", importableFiltered.length.toString())}
            </button>
          </div>
        )}
        {selectAllScope === "all" && (
          <div className="mt-1 py-1.5 px-3 bg-blue-50 dark:bg-blue-950 text-sm text-center rounded">
            <span className="text-blue-800 dark:text-blue-200">
              {t("admin.githubImport.allMatchingSelected").replace("{count}", importableFiltered.length.toString())}
            </span>{" "}
            <button
              type="button"
              className="text-blue-600 dark:text-blue-400 font-medium hover:underline"
              onClick={handleClearSelection}
            >
              {t("admin.githubImport.clearSelection")}
            </button>
          </div>
        )}
      </div>

      {/* Repository List */}
      {repoLoading && allRepos.length === 0 ? (
        <div className="flex items-center justify-center py-8">
          <Loader2 className="h-6 w-6 animate-spin text-primary" />
        </div>
      ) : (
        <div className="space-y-1">
          {pagedRepos.length === 0 && (searchQuery || languageFilter !== "all" || importStatusFilter !== "all") && (
            <p className="text-sm text-muted-foreground text-center py-8">
              {t("admin.githubImport.noResults")}
            </p>
          )}
          {pagedRepos.map((repo) => (
            <div
              key={repo.fullName}
              className={`flex items-center gap-3 p-3 rounded-lg border transition-colors ${
                repo.alreadyImported
                  ? "opacity-50 bg-muted"
                  : selectedRepos.has(repo.fullName)
                  ? "border-primary bg-primary/5"
                  : "hover:bg-muted"
              }`}
            >
              <Checkbox
                checked={selectedRepos.has(repo.fullName)}
                disabled={repo.alreadyImported}
                onCheckedChange={() => toggleRepo(repo.fullName)}
              />
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <GitBranch className="h-4 w-4 text-muted-foreground" />
                  <span className="font-medium truncate">{repo.fullName}</span>
                  {repo.private ? (
                    <Lock className="h-3 w-3 text-amber-500" />
                  ) : (
                    <Globe className="h-3 w-3 text-green-500" />
                  )}
                  {repo.alreadyImported && (
                    <Badge variant="secondary" className="text-xs">
                      {t("admin.githubImport.alreadyImported")}
                    </Badge>
                  )}
                </div>
                {repo.description && (
                  <p className="text-xs text-muted-foreground mt-0.5 truncate">
                    {repo.description}
                  </p>
                )}
              </div>
              <div className="flex items-center gap-3 text-xs text-muted-foreground shrink-0">
                {repo.language && (
                  <span className="px-2 py-0.5 bg-muted rounded text-xs">
                    {repo.language}
                  </span>
                )}
                <span className="flex items-center gap-1">
                  <Star className="h-3 w-3" />
                  {repo.stargazersCount}
                </span>
                <span className="flex items-center gap-1">
                  <GitFork className="h-3 w-3" />
                  {repo.forksCount}
                </span>
                <a
                  href={repo.htmlUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="hover:text-primary"
                  onClick={(e) => e.stopPropagation()}
                >
                  <ExternalLink className="h-3 w-3" />
                </a>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Pagination */}
      {totalFilteredPages > 1 && (
        <div className="flex items-center justify-between pt-2">
          <Button
            variant="outline"
            size="sm"
            disabled={page <= 1}
            onClick={() => setPage((p) => p - 1)}
          >
            {t("admin.githubImport.prevPage")}
          </Button>
          <span className="text-sm text-muted-foreground">
            {page} / {totalFilteredPages}
          </span>
          <Button
            variant="outline"
            size="sm"
            disabled={page >= totalFilteredPages}
            onClick={() => setPage((p) => p + 1)}
          >
            {t("admin.githubImport.nextPage")}
          </Button>
        </div>
      )}

      {/* Import Button */}
      <div className="flex items-center justify-between pt-4 border-t">
        <span className="text-sm text-muted-foreground">
          {selectedRepos.size > 0
            ? t("admin.githubImport.readyToImport").replace(
                "{count}",
                selectedRepos.size.toString()
              )
            : t("admin.githubImport.selectReposPrompt")}
        </span>
        <Button
          onClick={handleImport}
          disabled={
            selectedRepos.size === 0 || !isDepartmentValid || importing
          }
        >
          {importing ? (
            <Loader2 className="h-4 w-4 mr-2 animate-spin" />
          ) : (
            <Download className="h-4 w-4 mr-2" />
          )}
          {importing
            ? t("admin.githubImport.importing")
            : t("admin.githubImport.importButton").replace(
                "{count}",
                selectedRepos.size.toString()
              )}
        </Button>
      </div>

      {/* Import Result */}
      {importResult && (
        <Card className="border-green-200 bg-green-50 dark:border-green-900 dark:bg-green-950">
          <CardContent className="pt-4">
            <div className="flex items-start gap-3">
              <CheckCircle2 className="h-5 w-5 text-green-600 mt-0.5" />
              <div className="space-y-1">
                <p className="font-medium text-green-800 dark:text-green-200">
                  {t("admin.githubImport.importComplete")}
                </p>
                <p className="text-sm text-green-700 dark:text-green-300">
                  {importResult.imported} {t("admin.githubImport.imported")},{" "}
                  {importResult.skipped} {t("admin.githubImport.skipped")}
                </p>
                {importResult.skippedRepos.length > 0 && (
                  <p className="text-xs text-green-600 dark:text-green-400">
                    {t("admin.githubImport.skippedList")}:{" "}
                    {importResult.skippedRepos.join(", ")}
                  </p>
                )}
              </div>
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
