"use client";

import { useCallback, useEffect, useState } from "react";
import { useTranslations } from "@/hooks/use-translations";
import { toast } from "sonner";
import { AppLayout } from "@/components/app-layout";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { AlertCircle, ArrowLeft, Loader2, RefreshCw } from "lucide-react";
import Link from "next/link";
import {
  getUserGitHubStatus,
  getUserInstallationRepos,
  userImportRepos,
  type UserGitHubStatus,
  type GitHubInstallation,
} from "@/lib/github-import-api";
import { getMyDepartments, type UserDepartment } from "@/lib/organization-api";
import { GitHubInstallationList } from "@/components/github/github-installation-list";
import { GitHubRepoBrowser } from "@/components/github/github-repo-browser";

export default function UserGitHubImportPage() {
  const t = useTranslations();
  const [status, setStatus] = useState<UserGitHubStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [selectedInstallation, setSelectedInstallation] = useState<GitHubInstallation | null>(null);
  const [departments, setDepartments] = useState<UserDepartment[]>([]);

  const fetchStatus = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getUserGitHubStatus();
      setStatus(result);
      if (result.installations.length > 0 && !selectedInstallation) {
        setSelectedInstallation(result.installations[0]);
      }
    } catch {
      toast.error(t("admin.githubImport.fetchStatusFailed"));
    } finally {
      setLoading(false);
    }
  }, [t, selectedInstallation]);

  const fetchDepartments = useCallback(async () => {
    try {
      const result = await getMyDepartments();
      setDepartments(result);
    } catch {
      // Departments may not be available
    }
  }, []);

  useEffect(() => {
    fetchStatus();
    fetchDepartments();
  }, []);

  const handleImport = useCallback(async (params: {
    installationId: number;
    departmentId: string;
    languageCode: string;
    repos: any[];
  }) => {
    return userImportRepos({
      installationId: params.installationId,
      departmentId: params.departmentId || undefined,
      languageCode: params.languageCode,
      repos: params.repos,
    });
  }, []);

  if (loading) {
    return (
      <AppLayout activeItem={t("sidebar.private")}>
        <div className="flex items-center justify-center min-h-[400px]">
          <Loader2 className="h-8 w-8 animate-spin text-primary" />
        </div>
      </AppLayout>
    );
  }

  return (
    <AppLayout activeItem={t("sidebar.private")}>
      <div className="flex flex-1 flex-col gap-6 p-4 md:p-6">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-4">
            <Link href="/private">
              <Button variant="ghost" size="sm">
                <ArrowLeft className="h-4 w-4 mr-2" />
                {t("home.githubImport.backToPrivate")}
              </Button>
            </Link>
            <div>
              <h1 className="text-2xl font-bold">{t("home.githubImport.title")}</h1>
              <p className="text-muted-foreground mt-1">
                {t("home.githubImport.description")}
              </p>
            </div>
          </div>
          <Button variant="outline" size="sm" onClick={fetchStatus}>
            <RefreshCw className="h-4 w-4 mr-2" />
            {t("admin.githubImport.refresh")}
          </Button>
        </div>

        {/* Not available message */}
        {!status?.available ? (
          <Card>
            <CardContent className="pt-6">
              <div className="flex items-center gap-3 text-amber-600">
                <AlertCircle className="h-5 w-5" />
                <span>{t("home.githubImport.notAvailable")}</span>
              </div>
            </CardContent>
          </Card>
        ) : (
          <>
            {/* Connected Organizations */}
            <Card>
              <CardHeader>
                <CardTitle className="text-lg">{t("admin.githubImport.connectedOrgs")}</CardTitle>
              </CardHeader>
              <CardContent>
                <GitHubInstallationList
                  installations={status.installations}
                  selectedInstallation={selectedInstallation}
                  onSelect={setSelectedInstallation}
                />
              </CardContent>
            </Card>

            {/* Repository Browser */}
            {selectedInstallation && (
              <GitHubRepoBrowser
                installation={selectedInstallation}
                fetchRepos={getUserInstallationRepos}
                departments={departments.map(d => ({ id: d.id, name: d.name }))}
                onImport={handleImport}
                showPersonalOption={true}
              />
            )}

            {/* No departments hint */}
            {departments.length === 0 && (
              <p className="text-sm text-muted-foreground">
                {t("home.githubImport.noDepartments")}
              </p>
            )}
          </>
        )}
      </div>
    </AppLayout>
  );
}
