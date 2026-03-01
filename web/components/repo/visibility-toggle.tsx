"use client";

import * as React from "react";
import { Switch } from "@/components/ui/switch";
import { Spinner } from "@/components/ui/spinner";
import { Globe, Lock } from "lucide-react";
import { cn } from "@/lib/utils";
import { updateRepositoryVisibility } from "@/lib/repository-api";
import { toast } from "sonner";
import { useTranslations } from "@/hooks/use-translations";

export interface VisibilityToggleProps {
  repositoryId: string;
  isPublic: boolean;
  onVisibilityChange: (newIsPublic: boolean) => void;
  disabled?: boolean;
}

export function VisibilityToggle({
  repositoryId,
  isPublic,
  onVisibilityChange,
  disabled = false,
}: VisibilityToggleProps) {
  const t = useTranslations();
  const [isLoading, setIsLoading] = React.useState(false);
  const [currentIsPublic, setCurrentIsPublic] = React.useState(isPublic);

  // Sync external state changes
  React.useEffect(() => {
    setCurrentIsPublic(isPublic);
  }, [isPublic]);

  const isDisabled = disabled || isLoading;

  const handleToggle = async (checked: boolean) => {
    const newIsPublic = checked;

    setIsLoading(true);

    try {
      const response = await updateRepositoryVisibility({
        repositoryId,
        isPublic: newIsPublic,
      });

      if (response.success) {
        setCurrentIsPublic(response.isPublic);
        onVisibilityChange(response.isPublic);
        toast.success(
          response.isPublic
            ? t("home.private.visibility.setSharedSuccess")
            : t("home.private.visibility.setRestrictedSuccess")
        );
      } else {
        toast.error(response.errorMessage || t("home.private.visibility.updateError"));
      }
    } catch (error) {
      console.error("Failed to update visibility:", error);
      toast.error(t("home.private.visibility.updateError"));
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div
      className={cn(
        "flex items-center",
        isDisabled && "opacity-50"
      )}
    >
      <div className="flex items-center gap-2">
        {isLoading ? (
          <Spinner className="h-4 w-4" />
        ) : currentIsPublic ? (
          <Globe className="h-4 w-4 text-muted-foreground" />
        ) : (
          <Lock className="h-4 w-4 text-muted-foreground" />
        )}
        <Switch
          checked={currentIsPublic}
          onCheckedChange={handleToggle}
          disabled={isDisabled}
          aria-label={currentIsPublic ? t("home.private.visibility.shared") : t("home.private.visibility.restricted")}
        />
        <span className="text-sm text-muted-foreground">
          {currentIsPublic ? t("home.private.visibility.shared") : t("home.private.visibility.restricted")}
        </span>
      </div>
    </div>
  );
}
