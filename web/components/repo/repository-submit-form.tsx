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
import { submitRepository, fetchGitBranches, checkGitHubRepo } from "@/lib/repository-api";
import type { RepositorySubmitRequest, GitBranchItem } from "@/types/repository";
import { Loader2, GitBranch, Globe, Lock, Link2, FolderGit2, Search, Edit3 } from "lucide-react";
import { toast } from "sonner";

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

function parseGitUrl(url: string): { orgName: string; repoName: string } | null {
  const httpsMatch = url.match(/https?:\/\/[^/]+\/([^/]+)\/([^/]+?)(?:\.git)?$/i);
  if (httpsMatch) {
    return { orgName: httpsMatch[1], repoName: httpsMatch[2] };
  }
  
  const sshMatch = url.match(/git@[^:]+:([^/]+)\/([^/]+?)(?:\.git)?$/i);
  if (sshMatch) {
    return { orgName: sshMatch[1], repoName: sshMatch[2] };
  }
  
  return null;
}

export function RepositorySubmitForm({ onSuccess }: RepositorySubmitFormProps) {
  const t = useTranslations();
  
  const [gitUrl, setGitUrl] = useState("");
  const [branchName, setBranchName] = useState("main");
  const [languageCode, setLanguageCode] = useState("en");
  const [isPublic, setIsPublic] = useState(true);
  const [authAccount, setAuthAccount] = useState("");
  const [authPassword, setAuthPassword] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});
  
  // Branch selection states
  const [branches, setBranches] = useState<GitBranchItem[]>([]);
  const [isLoadingBranches, setIsLoadingBranches] = useState(false);
  const [isSupported, setIsSupported] = useState(true);
  const [isManualInput, setIsManualInput] = useState(false);
  const [branchSearch, setBranchSearch] = useState("");
  const lastFetchedUrl = useRef<string>("");

  // Debounced branch fetching
  const fetchBranchesDebounced = useCallback(async (url: string) => {
    if (!url.trim() || !GIT_URL_REGEX.test(url.trim())) {
      setBranches([]);
      setIsSupported(true);
      return;
    }

    // Avoid duplicate fetches
    if (lastFetchedUrl.current === url.trim()) {
      return;
    }
    lastFetchedUrl.current = url.trim();

    setIsLoadingBranches(true);
    try {
      const result = await fetchGitBranches(url.trim());
      setBranches(result.branches);
      setIsSupported(result.isSupported);
      
      // Set default branch if available
      if (result.defaultBranch) {
        setBranchName(result.defaultBranch);
      } else if (result.branches.length > 0) {
        const defaultBranch = result.branches.find(b => b.isDefault);
        if (defaultBranch) {
          setBranchName(defaultBranch.name);
        }
      }
      
      // If not supported, switch to manual input
      if (!result.isSupported) {
        setIsManualInput(true);
      }

      // Check repo visibility and auto-set isPublic
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
  }, []);

  // Fetch branches when git URL changes
  useEffect(() => {
    const timer = setTimeout(() => {
      if (gitUrl.trim() && GIT_URL_REGEX.test(gitUrl.trim())) {
        fetchBranchesDebounced(gitUrl);
      }
    }, 500);

    return () => clearTimeout(timer);
  }, [gitUrl, fetchBranchesDebounced]);

  // Filter branches by search
  const filteredBranches = branches.filter(b => 
    b.name.toLowerCase().includes(branchSearch.toLowerCase())
  );

  const validateForm = (): boolean => {
    const newErrors: Record<string, string> = {};

    if (!gitUrl.trim()) {
      newErrors.gitUrl = t("home.repository.gitUrlRequired");
    } else if (!GIT_URL_REGEX.test(gitUrl.trim())) {
      newErrors.gitUrl = t("home.repository.gitUrlInvalid");
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

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    
    if (!validateForm()) return;

    const parsed = parseGitUrl(gitUrl.trim());
    if (!parsed) {
      setErrors({ gitUrl: t("home.repository.gitUrlInvalid") });
      return;
    }

    setIsSubmitting(true);

    try {
      // Use the isPublic state which may have been auto-set by visibility check
      const effectiveIsPublic = isPublic;

      const request: RepositorySubmitRequest = {
        gitUrl: gitUrl.trim(),
        repoName: parsed.repoName,
        orgName: parsed.orgName,
        branchName: branchName.trim(),
        languageCode,
        isPublic: effectiveIsPublic,
        authAccount: authAccount.trim() || undefined,
        authPassword: authPassword || undefined,
      };

      await submitRepository(request);
      toast.success(t("home.repository.submitSuccess"));
      
      setGitUrl("");
      setBranchName("main");
      setLanguageCode("en");
      setIsPublic(true);
      setAuthAccount("");
      setAuthPassword("");
      setErrors({});
      setBranches([]);
      setIsManualInput(false);
      setBranchSearch("");
      lastFetchedUrl.current = "";
      
      onSuccess?.();
    } catch (error) {
      toast.error(t("home.repository.submitError"));
      console.error("Failed to submit repository:", error);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-5">
      {/* Header */}
      <div className="flex items-center gap-3 pb-2">
        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-gradient-to-br from-teal-500 to-emerald-500 text-white shadow-lg shadow-teal-500/25">
          <FolderGit2 className="h-5 w-5" />
        </div>
        <div>
          <h2 className="text-lg font-semibold">{t("home.repository.submitTitle")}</h2>
          <p className="text-sm text-muted-foreground">{t("home.repository.submitDescription")}</p>
        </div>
      </div>

      {/* Git URL */}
      <div className="space-y-2">
        <label className="text-sm font-medium flex items-center gap-2">
          <Link2 className="h-4 w-4 text-muted-foreground" />
          {t("home.repository.gitUrl")}
        </label>
        <Input
          value={gitUrl}
          onChange={(e) => setGitUrl(e.target.value)}
          placeholder={t("home.repository.gitUrlPlaceholder")}
          aria-invalid={!!errors.gitUrl}
          className="h-11 bg-secondary/50 border-transparent focus:border-primary/50 transition-colors"
        />
        {errors.gitUrl && (
          <p className="text-sm text-destructive">{errors.gitUrl}</p>
        )}
      </div>

      {/* Branch Name */}
      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <label className="text-sm font-medium flex items-center gap-2">
            <GitBranch className="h-4 w-4 text-muted-foreground" />
            {t("home.repository.branchName")}
          </label>
          {branches.length > 0 && (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="h-6 px-2 text-xs"
              onClick={() => setIsManualInput(!isManualInput)}
            >
              <Edit3 className="h-3 w-3 mr-1" />
              {isManualInput ? t("home.repository.selectBranch") : t("home.repository.manualInput")}
            </Button>
          )}
        </div>
        
        {isLoadingBranches ? (
          <div className="flex items-center gap-2 h-11 px-3 bg-secondary/50 rounded-md">
            <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
            <span className="text-sm text-muted-foreground">{t("home.repository.loadingBranches")}</span>
          </div>
        ) : isManualInput || !isSupported || branches.length === 0 ? (
          <Input
            value={branchName}
            onChange={(e) => setBranchName(e.target.value)}
            placeholder={t("home.repository.branchNamePlaceholder")}
            aria-invalid={!!errors.branchName}
            className="h-11 bg-secondary/50 border-transparent focus:border-primary/50 transition-colors"
          />
        ) : (
          <Select value={branchName} onValueChange={setBranchName}>
            <SelectTrigger className="w-full h-11 bg-secondary/50 border-transparent focus:border-primary/50 transition-colors">
              <SelectValue placeholder={t("home.repository.selectBranchPlaceholder")} />
            </SelectTrigger>
            <SelectContent>
              {branches.length > 10 && (
                <div className="px-2 pb-2">
                  <div className="relative">
                    <Search className="absolute left-2 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
                    <Input
                      value={branchSearch}
                      onChange={(e) => setBranchSearch(e.target.value)}
                      placeholder={t("home.repository.searchBranch")}
                      className="h-8 pl-8 text-sm"
                      onClick={(e) => e.stopPropagation()}
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
                          <span className="text-xs px-1.5 py-0.5 rounded bg-primary/10 text-primary">
                            default
                          </span>
                        )}
                      </span>
                    </SelectItem>
                  ))
                )}
              </div>
              {filteredBranches.length > 0 && branchSearch && !filteredBranches.find(b => b.name === branchSearch) && (
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
                    <Edit3 className="h-3 w-3 mr-2" />
                    {t("home.repository.useCustomBranch")}: {branchSearch}
                  </Button>
                </div>
              )}
            </SelectContent>
          </Select>
        )}
        
        {!isSupported && gitUrl && GIT_URL_REGEX.test(gitUrl) && (
          <p className="text-xs text-muted-foreground">
            {t("home.repository.branchNotSupported")}
          </p>
        )}
        {errors.branchName && (
          <p className="text-sm text-destructive">{errors.branchName}</p>
        )}
      </div>

      {/* Language */}
      <div className="space-y-2">
        <label className="text-sm font-medium">
          {t("home.repository.language")}
        </label>
        <Select value={languageCode} onValueChange={setLanguageCode}>
          <SelectTrigger className="w-full h-11 bg-secondary/50 border-transparent focus:border-primary/50 transition-colors">
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
        {errors.languageCode && (
          <p className="text-sm text-destructive">{errors.languageCode}</p>
        )}
      </div>

      {/* Public/Private Toggle */}
      <div className="flex items-center justify-between rounded-xl bg-secondary/50 p-4">
        <div className="flex items-center gap-3">
          <div className={`flex h-9 w-9 items-center justify-center rounded-lg ${isPublic ? 'bg-blue-500/10 text-blue-500' : 'bg-amber-500/10 text-amber-500'}`}>
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

      {/* Auth fields */}
      {!isPublic && (
        <div className="space-y-4 rounded-xl border border-amber-500/20 bg-amber-500/5 p-4">
          <p className="text-xs text-amber-600 dark:text-amber-400 font-medium">
            {t("home.repository.authHint")}
          </p>
          <div className="space-y-2">
            <label className="text-sm font-medium">
              {t("home.repository.authAccount")}
            </label>
            <Input
              value={authAccount}
              onChange={(e) => setAuthAccount(e.target.value)}
              placeholder={t("home.repository.authAccountPlaceholder")}
              className="h-11 bg-background/50 border-transparent focus:border-primary/50"
            />
          </div>
          <div className="space-y-2">
            <label className="text-sm font-medium">
              {t("home.repository.authPassword")}
            </label>
            <Input
              type="password"
              value={authPassword}
              onChange={(e) => setAuthPassword(e.target.value)}
              placeholder={t("home.repository.authPasswordPlaceholder")}
              className="h-11 bg-background/50 border-transparent focus:border-primary/50"
            />
          </div>
        </div>
      )}

      {/* Submit Button */}
      <Button 
        type="submit" 
        className="w-full h-11 bg-gradient-to-r from-teal-500 to-emerald-500 hover:from-teal-600 hover:to-emerald-600 text-white shadow-lg shadow-teal-500/25 transition-all hover:shadow-teal-500/40" 
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
