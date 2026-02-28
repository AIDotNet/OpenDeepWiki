"use client";

import { useCallback, useEffect, useState } from "react";
import { useTranslations } from "@/hooks/use-translations";
import { toast } from "sonner";
import {
  getGitHubStatus,
  getGitHubInstallUrl,
  getGitHubConfig,
  saveGitHubConfig,
  resetGitHubConfig,
  getInstallationRepos,
  batchImportRepos,
  getDepartments,
  disconnectGitHubInstallation,
  GitHubStatus,
  GitHubConfig,
  GitHubInstallation,
  AdminDepartment,
} from "@/lib/admin-api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import {
  RefreshCw,
  ExternalLink,
  Loader2,
  CheckCircle2,
  AlertCircle,
  Plus,
  Key,
  Save,
  HelpCircle,
  Info,
  Unlink,
  RotateCcw,
  Upload,
} from "lucide-react";
import { GitHubInstallationList } from "@/components/github/github-installation-list";
import { GitHubRepoBrowser } from "@/components/github/github-repo-browser";

export default function GitHubImportPage() {
  const t = useTranslations();

  // Status
  const [status, setStatus] = useState<GitHubStatus | null>(null);
  const [loading, setLoading] = useState(true);

  // Selected installation
  const [selectedInstallation, setSelectedInstallation] = useState<GitHubInstallation | null>(null);

  // Departments
  const [departments, setDepartments] = useState<AdminDepartment[]>([]);

  // GitHub App configuration form
  const [config, setConfig] = useState<GitHubConfig | null>(null);
  const [configForm, setConfigForm] = useState({
    appId: "",
    appName: "",
    privateKey: "",
  });
  const [saving, setSaving] = useState(false);
  const [disconnecting, setDisconnecting] = useState<string | null>(null);
  const [resetting, setResetting] = useState(false);

  const fetchConfig = useCallback(async () => {
    try {
      const result = await getGitHubConfig();
      setConfig(result);
      if (result.appId) setConfigForm((f) => ({ ...f, appId: result.appId || "" }));
      if (result.appName) setConfigForm((f) => ({ ...f, appName: result.appName || "" }));
    } catch {
      // Config endpoint may not be available
    }
  }, []);

  const handleSaveConfig = async () => {
    if (!configForm.appId.trim()) {
      toast.error(t("admin.githubImport.validationAppIdRequired"));
      return;
    }
    // When updating existing config, privateKey can be empty (keep existing)
    if (!config?.hasPrivateKey && !configForm.privateKey.trim()) {
      toast.error(t("admin.githubImport.validationPrivateKeyRequired"));
      return;
    }
    if (configForm.privateKey.trim() && !configForm.privateKey.includes("-----BEGIN")) {
      toast.error(t("admin.githubImport.validationPrivateKeyInvalid"));
      return;
    }

    setSaving(true);
    try {
      await saveGitHubConfig({
        appId: configForm.appId.trim(),
        appName: configForm.appName.trim(),
        privateKey: configForm.privateKey,
      });
      toast.success(t("admin.githubImport.configSaved"));
      setConfigForm((f) => ({ ...f, privateKey: "" }));
      // Refresh status and config to show the configured state
      await fetchStatus();
      await fetchConfig();
    } catch (error) {
      toast.error(
        error instanceof Error ? error.message : t("admin.githubImport.configSaveFailed")
      );
    } finally {
      setSaving(false);
    }
  };

  const fetchStatus = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getGitHubStatus();
      setStatus(result);
      if (result.installations.length > 0 && !selectedInstallation) {
        setSelectedInstallation(result.installations[0]);
      }
    } catch (error) {
      toast.error(t("admin.githubImport.fetchStatusFailed"));
    } finally {
      setLoading(false);
    }
  }, [t, selectedInstallation]);

  const fetchDepartments = useCallback(async () => {
    try {
      const result = await getDepartments();
      setDepartments(result);
    } catch {
      // Departments may not be available
    }
  }, []);

  useEffect(() => {
    fetchStatus();
    fetchDepartments();
    fetchConfig();
  }, []);

  const handleConnectNew = async () => {
    try {
      const { url } = await getGitHubInstallUrl();
      window.location.href = url;
    } catch (error) {
      toast.error("Failed to get install URL");
    }
  };

  const handleDisconnect = async (inst: GitHubInstallation) => {
    const confirmed = window.confirm(
      t("admin.githubImport.disconnectConfirm").replace("{org}", inst.accountLogin)
    );
    if (!confirmed) return;

    setDisconnecting(inst.id);
    try {
      await disconnectGitHubInstallation(inst.id);
      toast.success(t("admin.githubImport.disconnectSuccess"));
      if (selectedInstallation?.id === inst.id) {
        setSelectedInstallation(null);
      }
      await fetchStatus();
    } catch (error) {
      toast.error(
        error instanceof Error ? error.message : t("admin.githubImport.disconnectFailed")
      );
    } finally {
      setDisconnecting(null);
    }
  };

  const handleResetConfig = async () => {
    const confirmed = window.confirm(
      t("admin.githubImport.resetConfigConfirm")
    );
    if (!confirmed) return;

    setResetting(true);
    try {
      await resetGitHubConfig();
      toast.success(t("admin.githubImport.resetConfigSuccess"));
      setSelectedInstallation(null);
      setConfig(null);
      setConfigForm({ appId: "", appName: "", privateKey: "" });
      await fetchStatus();
      await fetchConfig();
    } catch (error) {
      toast.error(
        error instanceof Error ? error.message : t("admin.githubImport.resetConfigFailed")
      );
    } finally {
      setResetting(false);
    }
  };

  const handleImport = useCallback(async (params: {
    installationId: number;
    departmentId: string;
    languageCode: string;
    repos: {
      fullName: string;
      name: string;
      owner: string;
      cloneUrl: string;
      defaultBranch: string;
      private: boolean;
      language?: string;
      stargazersCount: number;
      forksCount: number;
    }[];
  }) => {
    return batchImportRepos(params);
  }, []);

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">{t("admin.githubImport.title")}</h1>
          <p className="text-muted-foreground mt-1">
            {t("admin.githubImport.description")}
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={fetchStatus}>
          <RefreshCw className="h-4 w-4 mr-2" />
          {t("admin.githubImport.refresh")}
        </Button>
      </div>

      {/* Status Card */}
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t("admin.githubImport.connectionStatus")}</CardTitle>
        </CardHeader>
        <CardContent>
          {!status?.configured ? (
            <div className="flex items-center gap-2 text-amber-600">
              <AlertCircle className="h-5 w-5" />
              <span>{t("admin.githubImport.credentialSourceNone")}</span>
            </div>
          ) : (
            <div className="space-y-4">
              <div className="flex items-center gap-2 text-green-600">
                <CheckCircle2 className="h-5 w-5" />
                <span>{t("admin.githubImport.configured")}</span>
                {config && (
                  <span className="text-xs text-muted-foreground ml-2">
                    ({config.source === "database"
                      ? t("admin.githubImport.credentialSourceDb")
                      : t("admin.githubImport.credentialSourceEnv")})
                  </span>
                )}
              </div>

              {/* Connected Organizations */}
              {status.installations.length > 0 && (
                <div className="space-y-2">
                  <h3 className="text-sm font-medium">
                    {t("admin.githubImport.connectedOrgs")}
                  </h3>
                  <GitHubInstallationList
                    installations={status.installations}
                    selectedInstallation={selectedInstallation}
                    onSelect={setSelectedInstallation}
                    renderActions={(inst) => (
                      <Button
                        variant="outline"
                        size="sm"
                        className="h-7 px-2 text-muted-foreground hover:text-destructive hover:border-destructive"
                        onClick={(e) => {
                          e.stopPropagation();
                          handleDisconnect(inst);
                        }}
                        disabled={disconnecting === inst.id}
                      >
                        {disconnecting === inst.id ? (
                          <Loader2 className="h-4 w-4 animate-spin" />
                        ) : (
                          <>
                            <Unlink className="h-4 w-4 mr-1" />
                            <span className="text-xs">{t("admin.githubImport.disconnectOrg")}</span>
                          </>
                        )}
                      </Button>
                    )}
                  />
                </div>
              )}

              <div className="flex items-center gap-2">
                <Button variant="outline" size="sm" onClick={handleConnectNew}>
                  <Plus className="h-4 w-4 mr-2" />
                  {t("admin.githubImport.connectNew")}
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  className="text-muted-foreground hover:text-destructive hover:border-destructive"
                  onClick={handleResetConfig}
                  disabled={resetting}
                >
                  {resetting ? (
                    <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                  ) : (
                    <RotateCcw className="h-4 w-4 mr-2" />
                  )}
                  {t("admin.githubImport.resetConfig")}
                </Button>
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Repository Browser */}
      {selectedInstallation && (
        <Card>
          <CardContent className="pt-6">
            <GitHubRepoBrowser
              installation={selectedInstallation}
              fetchRepos={getInstallationRepos}
              departments={departments}
              onImport={handleImport}
              showPersonalOption={false}
            />
          </CardContent>
        </Card>
      )}

      {/* Configuration Form -- shown when not configured */}
      {status && !status.configured && (
        <Card>
          <CardHeader>
            <CardTitle className="text-lg flex items-center gap-2">
              <Key className="h-5 w-5" />
              {status.configured
                ? t("admin.githubImport.updateConfig")
                : t("admin.githubImport.configTitle")}
            </CardTitle>
            <CardDescription>
              {status.configured
                ? t("admin.githubImport.updateConfigDescription")
                : t("admin.githubImport.configDescription")}
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {config?.source === "environment" && !status.configured && (
              <div className="flex items-center gap-2 text-amber-600 text-sm">
                <Info className="h-4 w-4 shrink-0" />
                <span>{t("admin.githubImport.overrideEnvWarning")}</span>
              </div>
            )}

            <div className="grid gap-4 max-w-xl">
              <div className="space-y-2">
                <Label htmlFor="appId">{t("admin.githubImport.appIdLabel")}</Label>
                <Input
                  id="appId"
                  placeholder={t("admin.githubImport.appIdPlaceholder")}
                  value={configForm.appId}
                  onChange={(e) =>
                    setConfigForm((f) => ({ ...f, appId: e.target.value }))
                  }
                />
                <p className="text-xs text-muted-foreground">
                  {t("admin.githubImport.appIdHelp")}
                </p>
              </div>

              <div className="space-y-2">
                <Label htmlFor="appName">{t("admin.githubImport.appNameLabel")}</Label>
                <Input
                  id="appName"
                  placeholder={t("admin.githubImport.appNamePlaceholder")}
                  value={configForm.appName}
                  onChange={(e) =>
                    setConfigForm((f) => ({ ...f, appName: e.target.value }))
                  }
                />
                <p className="text-xs text-muted-foreground">
                  {t("admin.githubImport.appNameHelp")}
                </p>
              </div>

              <div className="space-y-2">
                <div className="flex items-center justify-between">
                  <Label htmlFor="privateKey">{t("admin.githubImport.privateKeyLabel")}</Label>
                  <Button
                    variant="outline"
                    size="sm"
                    className="h-7 text-xs"
                    onClick={() => document.getElementById("pemFileInput")?.click()}
                    type="button"
                  >
                    <Upload className="h-3 w-3 mr-1" />
                    {t("admin.githubImport.loadPemFile")}
                  </Button>
                  <input
                    id="pemFileInput"
                    type="file"
                    accept=".pem"
                    className="hidden"
                    onChange={(e) => {
                      const file = e.target.files?.[0];
                      if (!file) return;
                      const reader = new FileReader();
                      reader.onload = (ev) => {
                        const content = ev.target?.result as string;
                        setConfigForm((f) => ({ ...f, privateKey: content }));
                      };
                      reader.readAsText(file);
                      e.target.value = "";
                    }}
                  />
                </div>
                <Textarea
                  id="privateKey"
                  placeholder={t("admin.githubImport.privateKeyPlaceholder")}
                  value={configForm.privateKey}
                  onChange={(e) =>
                    setConfigForm((f) => ({ ...f, privateKey: e.target.value }))
                  }
                  rows={6}
                  className="font-mono text-xs"
                />
                <p className="text-xs text-muted-foreground">
                  {status.configured
                    ? t("admin.githubImport.privateKeyUpdateHelp")
                    : t("admin.githubImport.privateKeyHelp")}
                </p>
              </div>
            </div>

            <Button onClick={handleSaveConfig} disabled={saving}>
              {saving ? (
                <Loader2 className="h-4 w-4 mr-2 animate-spin" />
              ) : (
                <Save className="h-4 w-4 mr-2" />
              )}
              {saving
                ? t("admin.githubImport.savingConfig")
                : t("admin.githubImport.saveConfig")}
            </Button>

            {/* Required permissions info */}
            <div className="border-t pt-4 mt-4">
              <div className="flex items-start gap-2 text-sm text-muted-foreground">
                <HelpCircle className="h-4 w-4 mt-0.5 shrink-0" />
                <div className="space-y-1">
                  <p className="font-medium">{t("admin.githubImport.requiredPermissions")}</p>
                  <p>{t("admin.githubImport.requiredPermissionsText")}</p>
                </div>
              </div>
            </div>
          </CardContent>
        </Card>
      )}

      {/* Setup Guide */}
      {status && !status.configured && (
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">{t("admin.githubImport.setupGuide")}</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <ol className="list-decimal list-inside space-y-2 text-sm text-muted-foreground">
              <li>{t("admin.githubImport.step1")}</li>
              <li>{t("admin.githubImport.step2")}</li>
              <li>{t("admin.githubImport.step3")}</li>
              <li>{t("admin.githubImport.step4")}</li>
            </ol>
            <a
              href="https://docs.github.com/en/apps/creating-github-apps/registering-a-github-app/registering-a-github-app"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-1 text-sm text-primary hover:underline"
            >
              <ExternalLink className="h-3 w-3" />
              {t("admin.githubImport.createAppLink")}
            </a>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
