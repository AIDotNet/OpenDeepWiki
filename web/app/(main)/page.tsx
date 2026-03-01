"use client";

import { useState, useCallback } from "react";
import { useRouter } from "next/navigation";
import { AppLayout } from "@/components/app-layout";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Search, Plus, Flame, Puzzle } from "lucide-react";
import { IntegrationsDialog } from "@/components/integrations-dialog";
import { useTranslations } from "@/hooks/use-translations";
import { RepositorySubmitForm } from "@/components/repo/repository-submit-form";
import {
  Dialog,
  DialogContent,
} from "@/components/ui/dialog";
import { useAuth } from "@/contexts/auth-context";
import { useScrollPosition } from "@/hooks/use-scroll-position";
import { PublicRepositoryList } from "@/components/repo/public-repository-list";
import { cn } from "@/lib/utils";

export default function Home() {
  const t = useTranslations();
  const router = useRouter();
  const { user } = useAuth();
  const [activeItem, setActiveItem] = useState(t("sidebar.explore"));
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [isIntegrationsOpen, setIsIntegrationsOpen] = useState(false);
  const [keyword, setKeyword] = useState("");
  const { isScrolled } = useScrollPosition(100);

  const handleSubmitSuccess = useCallback(() => {
    setIsFormOpen(false);
  }, []);

  const handleAddRepoClick = useCallback(() => {
    if (!user) {
      router.push("/auth");
      return;
    }
    setIsFormOpen(true);
  }, [user, router]);

  return (
    <AppLayout
      activeItem={activeItem}
      onItemClick={setActiveItem}
      searchBox={{
        value: keyword,
        onChange: setKeyword,
        visible: isScrolled,
      }}
    >
      <div className="flex flex-1 flex-col p-4">
        {/* Hero Section with Main Search Box */}
        <div className="flex flex-col items-center justify-center py-12">
          <div className="w-full max-w-2xl space-y-8">
            <h1 className="text-center text-4xl font-medium tracking-tight text-foreground">
              {t("home.title")}
            </h1>
            {/* Main Search Box with fade animation */}
            <div
              className={cn(
                "relative transition-all duration-250 ease-in-out",
                isScrolled
                  ? "opacity-0 -translate-y-2 pointer-events-none"
                  : "opacity-100 translate-y-0 pointer-events-auto"
              )}
            >
              <div className="absolute left-4 top-1/2 -translate-y-1/2 text-muted-foreground">
                <Search className="h-5 w-5" />
              </div>
              <Input
                value={keyword}
                onChange={(e) => setKeyword(e.target.value)}
                placeholder={t("home.searchPlaceholder")}
                maxLength={100}
                className="h-14 rounded-full pl-12 text-lg shadow-sm transition-all hover:shadow-md focus-visible:ring-2 focus-visible:ring-primary/20 bg-secondary/50 border-transparent"
              />
            </div>

            <div className="flex flex-wrap items-center justify-center gap-3">
              <Dialog open={isFormOpen} onOpenChange={setIsFormOpen}>
                <Button
                  variant="secondary"
                  className="gap-2 rounded-full h-10 px-6 bg-teal-500/10 text-teal-500 hover:bg-teal-500/20 hover:text-teal-400 border border-teal-500/20"
                  onClick={handleAddRepoClick}
                >
                  <Plus className="h-4 w-4" />
                  {t("home.addPrivateRepo")}
                </Button>
                <DialogContent className="sm:max-w-md">
                  {user && (
                    <RepositorySubmitForm
                      onSuccess={handleSubmitSuccess}
                    />
                  )}
                </DialogContent>
              </Dialog>
              <Button variant="secondary" className="gap-2 rounded-full h-10 px-6 bg-blue-500/10 text-blue-500 hover:bg-blue-500/20 hover:text-blue-400 border border-blue-500/20">
                <Flame className="h-4 w-4" />
                {t("home.exploreTrending")}
              </Button>
            </div>
            <div className="flex justify-center">
              <Button
                variant="ghost"
                className="gap-2 text-muted-foreground hover:text-foreground"
                onClick={() => setIsIntegrationsOpen(true)}
              >
                <Puzzle className="h-4 w-4" />
                {t("home.mcpIntegration")}
              </Button>
            </div>
            <IntegrationsDialog
              open={isIntegrationsOpen}
              onOpenChange={setIsIntegrationsOpen}
            />
          </div>
        </div>

        {/* Public Repository List Section */}
        <div className="w-full max-w-6xl mx-auto mt-8">
          <PublicRepositoryList keyword={keyword} />
        </div>
      </div>
    </AppLayout>
  );
}
