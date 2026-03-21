"use client";

import { useState, useEffect, useCallback, useRef } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { useTranslations } from "@/hooks/use-translations";
import {
  submitRepository,
  submitArchiveRepository,
  submitLocalDirectoryRepository,
  fetchGitBranches,
  checkGitHubRepo,
} from "@/lib/repository-api";
import type {
  RepositorySubmitRequest,
  ArchiveRepositorySubmitRequest,
  LocalDirectoryRepositorySubmitRequest,
  GitBranchItem,
  RepositorySourceType,
} from "@/types/repository";
import {
  Loader2,
  GitBranch,
  Globe,
  Lock,
  Link2,
  FolderGit2,
  Search,
  Edit3,
  Archive,
  FolderOpen,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { toast } from "sonner";
import { ApiError } from "@/lib/api-client";

interface RepositorySubmitFormProps {
  onSuccess?: () => void;
}

const GIT_URL_REGEX = /^(https?:\/\/|git@)[\w.-]+[/:].+?(\.git)?$/i;

const SUPPORTED_LANGUAGES = [
  { code: "en", label: "languages.en" },
  { code: "zh", label: "languages.zh" },
  { code: "ja", label: "languages.ja" },
  { code: "ko", label: "languages.ko" },
];

const SOURCE_OPTIONS: Array<{
  value: RepositorySourceType;
  icon: typeof Link2;
  labelKey: string;
  descriptionKey: string;
}> = [
  {
    value: "Git",
    icon: Link2,
    labelKey: "sourceTypeGit",
    descriptionKey: "sourceTypeGitDescription",
  },
  {
    value: "Archive",
    icon: Archive,
    labelKey: "sourceTypeArchive",
    descriptionKey: "sourceTypeArchiveDescription",
  },
  {
    value: "LocalDirectory",
    icon: FolderOpen,
    labelKey: "sourceTypeLocal",
    descriptionKey: "sourceTypeLocalDescription",
  },
];

function parseGitUrl(url: string): { orgName: string; repoName: string } | null {
  const httpsMatch = url.match(/https?:\/\/[^/]+\/(.+?)\/([^/]+?)(?:\.git)?$/i);
  if (httpsMatch) {
    return { orgName: httpsMatch[1], repoName: httpsMatch[2] };
  }

  const sshMatch = url.match(/git@[^:]+:(.+?)\/([^/]+?)(?:\.git)?$/i);
  if (sshMatch) {
    return { orgName: sshMatch[1], repoName: sshMatch[2] };
  }

  return null;
}

export function RepositorySubmitForm({ onSuccess }: RepositorySubmitFormProps) {
  const t = useTranslations();

  const [sourceType, setSourceType] = useState<RepositorySourceType>("Git");
  const [gitUrl, setGitUrl] = useState("");
  const [orgName, setOrgName] = useState("");
  const [repoName, setRepoName] = useState("");
  const [localPath, setLocalPath] = useState("");
  const [archiveFile, setArchiveFile] = useState<File | null>(null);
  const [branchName, setBranchName] = useState("main");
  const [languageCode, setLanguageCode] = useState("en");
  const [isPublic, setIsPublic] = useState(true);
  const [authAccount, setAuthAccount] = useState("");
  const [authPassword, setAuthPassword] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [fileInputKey, setFileInputKey] = useState(0);

  const [branches, setBranches] = useState<GitBranchItem[]>([]);
  const [isLoadingBranches, setIsLoadingBranches] = useState(false);
  const [isSupported, setIsSupported] = useState(true);
  const [isManualInput, setIsManualInput] = useState(false);
  const [branchSearch, setBranchSearch] = useState("");
  const lastFetchedUrl = useRef<string>("");

  const resetGitBranchState = useCallback(() => {
    setBranches([]);
    setIsSupported(true);
    setIsManualInput(false);
    setBranchSearch("");
    lastFetchedUrl.current = "";
  }, []);

  const resetForm = useCallback(() => {
    setSourceType("Git");
    setGitUrl("");
    setOrgName("");
    setRepoName("");
    setLocalPath("");
    setArchiveFile(null);
    setBranchName("main");
    setLanguageCode("en");
    setIsPublic(true);
    setAuthAccount("");
    setAuthPassword("");
    setErrors({});
    setFileInputKey((current) => current + 1);
    resetGitBranchState();
  }, [resetGitBranchState]);

  const fetchBranchesDebounced = useCallback(async (url: string) => {
    if (!url.trim() || !GIT_URL_REGEX.test(url.trim())) {
      resetGitBranchState();
      return;
    }

    if (lastFetchedUrl.current === url.trim()) {
      return;
    }
    lastFetchedUrl.current = url.trim();

    setIsLoadingBranches(true);
    try {
      const result = await fetchGitBranches(url.trim());
      setBranches(result.branches);
      setIsSupported(result.isSupported);

      if (result.defaultBranch) {
        setBranchName(result.defaultBranch);
      } else if (result.branches.length > 0) {
        const defaultBranch = result.branches.find((branch) => branch.isDefault);
        if (defaultBranch) {
          setBranchName(defaultBranch.name);
        }
      }

      if (!result.isSupported) {
        setIsManualInput(true);
      }

      const parsed = parseGitUrl(url.trim());
      if (parsed) {
        try {
          const repoCheck = await checkGitHubRepo(parsed.orgName, parsed.repoName);
          if (repoCheck.exists && repoCheck.isPrivate) {
            setIsPublic(false);
          }
        } catch (checkError) {
          console.error("Failed to check repo visibility:", checkError);
        }
      }
    } catch (error) {
      console.error("Failed to fetch branches:", error);
      setIsSupported(false);
      setIsManualInput(true);
    } finally {
      setIsLoadingBranches(false);
    }
  }, [resetGitBranchState]);

  useEffect(() => {
    if (sourceType !== "Git") {
      resetGitBranchState();
      setAuthAccount("");
      setAuthPassword("");
      setIsPublic(false);
      return;
    }

    const timer = setTimeout(() => {
      if (gitUrl.trim() && GIT_URL_REGEX.test(gitUrl.trim())) {
        fetchBranchesDebounced(gitUrl);
      }
    }, 500);

    return () => clearTimeout(timer);
  }, [sourceType, gitUrl, fetchBranchesDebounced, resetGitBranchState]);

  useEffect(() => {
    if (sourceType !== "Git") {
      return;
    }

    const parsed = parseGitUrl(gitUrl.trim());
    if (!parsed) {
      return;
    }

    setOrgName((current) => current.trim() || parsed.orgName);
    setRepoName((current) => current.trim() || parsed.repoName);
  }, [gitUrl, sourceType]);

  const filteredBranches = branches.filter((branch) =>
    branch.name.toLowerCase().includes(branchSearch.toLowerCase())
  );

  const validateForm = (): boolean => {
    const newErrors: Record<string, string> = {};
    const parsedGitUrl = sourceType === "Git" ? parseGitUrl(gitUrl.trim()) : null;
    const effectiveOrgName = orgName.trim() || parsedGitUrl?.orgName || "";
    const effectiveRepoName = repoName.trim() || parsedGitUrl?.repoName || "";

    if (sourceType === "Git") {
      if (!gitUrl.trim()) {
        newErrors.gitUrl = t("home.repository.gitUrlRequired");
      } else if (!GIT_URL_REGEX.test(gitUrl.trim())) {
        newErrors.gitUrl = t("home.repository.gitUrlInvalid");
      }
    }

    if (!effectiveOrgName) {
      newErrors.orgName = t("home.repository.orgNameRequired");
    }

    if (!effectiveRepoName) {
      newErrors.repoName = t("home.repository.repoNameRequired");
    }

    if (sourceType === "Archive") {
      if (!archiveFile) {
        newErrors.archive = t("home.repository.archiveFileRequired");
      } else if (!archiveFile.name.toLowerCase().endsWith(".zip")) {
        newErrors.archive = t("home.repository.archiveFileInvalid");
      }
    }

    if (sourceType === "LocalDirectory" && !localPath.trim()) {
      newErrors.localPath = t("home.repository.localPathRequired");
    }

    if (!branchName.trim()) {
      newErrors.branchName = t("home.repository.branchNameRequired");
    }

    if (!languageCode) {
      newErrors.languageCode = t("home.repository.languageRequired");
    }

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();

    if (!validateForm()) {
      return;
    }

    const parsedGitUrl = sourceType === "Git" ? parseGitUrl(gitUrl.trim()) : null;
    const effectiveOrgName = orgName.trim() || parsedGitUrl?.orgName || "";
    const effectiveRepoName = repoName.trim() || parsedGitUrl?.repoName || "";

    setIsSubmitting(true);

    try {
      if (sourceType === "Git") {
        const request: RepositorySubmitRequest = {
          gitUrl: gitUrl.trim(),
          repoName: effectiveRepoName,
          orgName: effectiveOrgName,
          branchName: branchName.trim(),
          languageCode,
          isPublic,
          authAccount: authAccount.trim() || undefined,
          authPassword: authPassword || undefined,
        };

        await submitRepository(request);
      } else if (sourceType === "Archive" && archiveFile) {
        const request: ArchiveRepositorySubmitRequest = {
          repoName: effectiveRepoName,
          orgName: effectiveOrgName,
          branchName: branchName.trim(),
          languageCode,
          isPublic,
          archive: archiveFile,
        };

        await submitArchiveRepository(request);
      } else {
        const request: LocalDirectoryRepositorySubmitRequest = {
          repoName: effectiveRepoName,
          orgName: effectiveOrgName,
          localPath: localPath.trim(),
          branchName: branchName.trim(),
          languageCode,
          isPublic,
        };

        await submitLocalDirectoryRepository(request);
      }

      toast.success(t("home.repository.submitSuccess"));
      resetForm();
      onSuccess?.();
    } catch (error) {
      const message =
        error instanceof ApiError || error instanceof Error
          ? error.message
          : t("home.repository.submitError");
      toast.error(message || t("home.repository.submitError"));
      console.error("Failed to submit repository:", error);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-5">
      <div className="flex items-center gap-3 pb-2">
        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-gradient-to-br from-teal-500 to-emerald-500 text-white shadow-lg shadow-teal-500/25">
          <FolderGit2 className="h-5 w-5" />
        </div>
        <div>
          <h2 className="text-lg font-semibold">{t("home.repository.submitTitle")}</h2>
          <p className="text-sm text-muted-foreground">{t("home.repository.submitDescription")}</p>
        </div>
      </div>

      <div className="space-y-3">
        <label className="text-sm font-medium">{t("home.repository.sourceType")}</label>
        <div className="grid gap-2 sm:grid-cols-3">
          {SOURCE_OPTIONS.map((option) => {
            const Icon = option.icon;
            const isActive = sourceType === option.value;

            return (
              <button
                key={option.value}
                type="button"
                className={cn(
                  "rounded-xl border px-4 py-3 text-left transition-all",
                  isActive
                    ? "border-teal-500 bg-teal-500/10 shadow-sm"
                    : "border-border bg-secondary/40 hover:bg-secondary/70"
                )}
                onClick={() => setSourceType(option.value)}
              >
                <div className="flex items-center gap-2 text-sm font-medium">
                  <Icon className="h-4 w-4" />
                  {t(`home.repository.${option.labelKey}`)}
                </div>
                <p className="mt-1 text-xs text-muted-foreground">
                  {t(`home.repository.${option.descriptionKey}`)}
                </p>
              </button>
            );
          })}
        </div>
      </div>

      {sourceType === "Git" && (
        <div className="space-y-2">
          <label className="flex items-center gap-2 text-sm font-medium">
            <Link2 className="h-4 w-4 text-muted-foreground" />
            {t("home.repository.gitUrl")}
          </label>
          <Input
            value={gitUrl}
            onChange={(event) => setGitUrl(event.target.value)}
            placeholder={t("home.repository.gitUrlPlaceholder")}
            aria-invalid={!!errors.gitUrl}
            className="h-11 border-transparent bg-secondary/50 transition-colors focus:border-primary/50"
          />
          {errors.gitUrl && <p className="text-sm text-destructive">{errors.gitUrl}</p>}
        </div>
      )}

      {sourceType === "Archive" && (
        <div className="space-y-2">
          <label className="flex items-center gap-2 text-sm font-medium">
            <Archive className="h-4 w-4 text-muted-foreground" />
            {t("home.repository.archiveFile")}
          </label>
          <Input
            key={fileInputKey}
            type="file"
            accept=".zip,application/zip"
            onChange={(event) => setArchiveFile(event.target.files?.[0] ?? null)}
            aria-invalid={!!errors.archive}
            className="h-11 border-transparent bg-secondary/50 file:mr-3 file:border-0 file:bg-transparent file:text-sm file:font-medium"
          />
          <p className="text-xs text-muted-foreground">{t("home.repository.archiveHint")}</p>
          {errors.archive && <p className="text-sm text-destructive">{errors.archive}</p>}
        </div>
      )}

      {sourceType === "LocalDirectory" && (
        <div className="space-y-2">
          <label className="flex items-center gap-2 text-sm font-medium">
            <FolderOpen className="h-4 w-4 text-muted-foreground" />
            {t("home.repository.localPath")}
          </label>
          <Input
            value={localPath}
            onChange={(event) => setLocalPath(event.target.value)}
            placeholder={t("home.repository.localPathPlaceholder")}
            aria-invalid={!!errors.localPath}
            className="h-11 border-transparent bg-secondary/50 transition-colors focus:border-primary/50"
          />
          <p className="text-xs text-muted-foreground">{t("home.repository.localPathHint")}</p>
          {errors.localPath && <p className="text-sm text-destructive">{errors.localPath}</p>}
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-2">
          <label className="text-sm font-medium">{t("home.repository.orgName")}</label>
          <Input
            value={orgName}
            onChange={(event) => setOrgName(event.target.value)}
            placeholder={t("home.repository.orgNamePlaceholder")}
            aria-invalid={!!errors.orgName}
            className="h-11 border-transparent bg-secondary/50 transition-colors focus:border-primary/50"
          />
          {errors.orgName && <p className="text-sm text-destructive">{errors.orgName}</p>}
        </div>
        <div className="space-y-2">
          <label className="text-sm font-medium">{t("home.repository.repoName")}</label>
          <Input
            value={repoName}
            onChange={(event) => setRepoName(event.target.value)}
            placeholder={t("home.repository.repoNamePlaceholder")}
            aria-invalid={!!errors.repoName}
            className="h-11 border-transparent bg-secondary/50 transition-colors focus:border-primary/50"
          />
          {errors.repoName && <p className="text-sm text-destructive">{errors.repoName}</p>}
        </div>
      </div>

      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <label className="flex items-center gap-2 text-sm font-medium">
            <GitBranch className="h-4 w-4 text-muted-foreground" />
            {t("home.repository.branchName")}
          </label>
          {sourceType === "Git" && branches.length > 0 && (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="h-6 px-2 text-xs"
              onClick={() => setIsManualInput((current) => !current)}
            >
              <Edit3 className="mr-1 h-3 w-3" />
              {isManualInput ? t("home.repository.selectBranch") : t("home.repository.manualInput")}
            </Button>
          )}
        </div>

        {sourceType === "Git" && isLoadingBranches ? (
          <div className="flex h-11 items-center gap-2 rounded-md bg-secondary/50 px-3">
            <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
            <span className="text-sm text-muted-foreground">{t("home.repository.loadingBranches")}</span>
          </div>
        ) : sourceType === "Git" && !isManualInput && isSupported && branches.length > 0 ? (
          <Select value={branchName} onValueChange={setBranchName}>
            <SelectTrigger className="h-11 w-full border-transparent bg-secondary/50 transition-colors focus:border-primary/50">
              <SelectValue placeholder={t("home.repository.selectBranchPlaceholder")} />
            </SelectTrigger>
            <SelectContent>
              {branches.length > 10 && (
                <div className="px-2 pb-2">
                  <div className="relative">
                    <Search className="absolute left-2 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                    <Input
                      value={branchSearch}
                      onChange={(event) => setBranchSearch(event.target.value)}
                      placeholder={t("home.repository.searchBranch")}
                      className="h-8 pl-8 text-sm"
                      onClick={(event) => event.stopPropagation()}
                    />
                  </div>
                </div>
              )}
              <div className="max-h-[200px] overflow-y-auto">
                {filteredBranches.length === 0 ? (
                  <div className="px-2 py-4 text-center text-sm text-muted-foreground">
                    {t("home.repository.noBranchFound")}
                  </div>
                ) : (
                  filteredBranches.map((branch) => (
                    <SelectItem key={branch.name} value={branch.name}>
                      <span className="flex items-center gap-2">
                        {branch.name}
                        {branch.isDefault && (
                          <span className="rounded bg-primary/10 px-1.5 py-0.5 text-xs text-primary">
                            default
                          </span>
                        )}
                      </span>
                    </SelectItem>
                  ))
                )}
              </div>
              {filteredBranches.length > 0 &&
                branchSearch &&
                !filteredBranches.find((branch) => branch.name === branchSearch) && (
                  <div className="border-t px-2 py-2">
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      className="w-full justify-start text-sm"
                      onClick={() => {
                        setBranchName(branchSearch);
                        setIsManualInput(true);
                      }}
                    >
                      <Edit3 className="mr-2 h-3 w-3" />
                      {t("home.repository.useCustomBranch")}: {branchSearch}
                    </Button>
                  </div>
                )}
            </SelectContent>
          </Select>
        ) : (
          <Input
            value={branchName}
            onChange={(event) => setBranchName(event.target.value)}
            placeholder={t("home.repository.branchNamePlaceholder")}
            aria-invalid={!!errors.branchName}
            className="h-11 border-transparent bg-secondary/50 transition-colors focus:border-primary/50"
          />
        )}

        {sourceType === "Git" && !isSupported && gitUrl && GIT_URL_REGEX.test(gitUrl) && (
          <p className="text-xs text-muted-foreground">{t("home.repository.branchNotSupported")}</p>
        )}
        {errors.branchName && <p className="text-sm text-destructive">{errors.branchName}</p>}
      </div>

      <div className="space-y-2">
        <label className="text-sm font-medium">{t("home.repository.language")}</label>
        <Select value={languageCode} onValueChange={setLanguageCode}>
          <SelectTrigger className="h-11 w-full border-transparent bg-secondary/50 transition-colors focus:border-primary/50">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {SUPPORTED_LANGUAGES.map((lang) => (
              <SelectItem key={lang.code} value={lang.code}>
                {t(`home.repository.${lang.label}`)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        {errors.languageCode && <p className="text-sm text-destructive">{errors.languageCode}</p>}
      </div>

      <div className="flex items-center justify-between rounded-xl bg-secondary/50 p-4">
        <div className="flex items-center gap-3">
          <div
            className={cn(
              "flex h-9 w-9 items-center justify-center rounded-lg",
              isPublic ? "bg-blue-500/10 text-blue-500" : "bg-amber-500/10 text-amber-500"
            )}
          >
            {isPublic ? <Globe className="h-4 w-4" /> : <Lock className="h-4 w-4" />}
          </div>
          <div>
            <p className="text-sm font-medium">{t("home.repository.isPublic")}</p>
            <p className="text-xs text-muted-foreground">
              {isPublic ? t("home.repository.publicDesc") : t("home.repository.privateDesc")}
            </p>
          </div>
        </div>
        <Switch checked={isPublic} onCheckedChange={setIsPublic} />
      </div>

      {sourceType === "Git" && !isPublic && (
        <div className="space-y-4 rounded-xl border border-amber-500/20 bg-amber-500/5 p-4">
          <p className="text-xs font-medium text-amber-600 dark:text-amber-400">
            {t("home.repository.authHint")}
          </p>
          <div className="space-y-2">
            <label className="text-sm font-medium">{t("home.repository.authAccount")}</label>
            <Input
              value={authAccount}
              onChange={(event) => setAuthAccount(event.target.value)}
              placeholder={t("home.repository.authAccountPlaceholder")}
              className="h-11 border-transparent bg-background/50 focus:border-primary/50"
            />
          </div>
          <div className="space-y-2">
            <label className="text-sm font-medium">{t("home.repository.authPassword")}</label>
            <Input
              type="password"
              value={authPassword}
              onChange={(event) => setAuthPassword(event.target.value)}
              placeholder={t("home.repository.authPasswordPlaceholder")}
              className="h-11 border-transparent bg-background/50 focus:border-primary/50"
            />
          </div>
        </div>
      )}

      <Button
        type="submit"
        className="h-11 w-full bg-gradient-to-r from-teal-500 to-emerald-500 text-white shadow-lg shadow-teal-500/25 transition-all hover:from-teal-600 hover:to-emerald-600 hover:shadow-teal-500/40"
        disabled={isSubmitting}
      >
        {isSubmitting ? (
          <>
            <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            {t("home.repository.submitting")}
          </>
        ) : (
          t("home.repository.submit")
        )}
      </Button>
    </form>
  );
}
