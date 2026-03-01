"use client";

import { useState, useCallback } from "react";
import { AppLayout } from "@/components/app-layout";
import { useTranslations } from "@/hooks/use-translations";
import { Button } from "@/components/ui/button";
import { Plus } from "lucide-react";
import { RepositorySubmitForm } from "@/components/repo/repository-submit-form";
import { RepositoryList } from "@/components/repo/repository-list";
import {
  Sheet,
  SheetContent,
  SheetTrigger,
} from "@/components/animate-ui/components/radix/sheet";
import { useAuth } from "@/contexts/auth-context";
import Link from "next/link";

const GithubIcon = ({ className }: { className?: string }) => (
  <svg className={className} viewBox="0 0 24 24" fill="currentColor" width="16" height="16">
    <path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z" />
  </svg>
);

export default function PrivatePage() {
  const t = useTranslations();
  const { user } = useAuth();
  const [activeItem, setActiveItem] = useState(t("sidebar.private"));
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [refreshTrigger, setRefreshTrigger] = useState(0);

  const handleSubmitSuccess = useCallback(() => {
    setIsFormOpen(false);
    setRefreshTrigger((prev) => prev + 1);
  }, []);

  // Use a placeholder user ID if not authenticated
  const ownerUserId = user?.id ?? "anonymous";

  return (
    <AppLayout activeItem={activeItem} onItemClick={setActiveItem}>
      <div className="flex flex-1 flex-col gap-4 p-4 md:p-6">
        <div className="flex items-center justify-between">
          <div className="space-y-2">
            <h1 className="text-3xl font-bold tracking-tight">{t("sidebar.private")}</h1>
            <p className="text-muted-foreground">
              {t("common.privateRepos.description")}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <Link href="/private/github-import">
              <Button variant="outline" className="gap-2">
                <GithubIcon className="h-4 w-4" />
                {t("home.importFromGitHub")}
              </Button>
            </Link>
            <Sheet open={isFormOpen} onOpenChange={setIsFormOpen}>
              <SheetTrigger asChild>
                <Button className="gap-2">
                  <Plus className="h-4 w-4" />
                  {t("home.addPrivateRepo")}
                </Button>
              </SheetTrigger>
            <SheetContent side="right" className="w-full sm:max-w-lg overflow-y-auto">
              <div className="pt-6">
                <RepositorySubmitForm
                  onSuccess={handleSubmitSuccess}
                />
              </div>
            </SheetContent>
            </Sheet>
          </div>
        </div>

        <RepositoryList ownerId={ownerUserId} refreshTrigger={refreshTrigger} />
      </div>
    </AppLayout>
  );
}
