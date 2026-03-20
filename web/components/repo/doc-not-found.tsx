"use client";

import { FileQuestion } from "lucide-react";
import { useTranslations } from "@/hooks/use-translations";

interface DocNotFoundProps {
  slug: string;
}

export function DocNotFound({ slug }: DocNotFoundProps) {
  const t = useTranslations();

  return (
    <div className="flex flex-col items-center justify-center py-20 px-4">
      <div className="rounded-full bg-muted/50 p-4 mb-6">
        <FileQuestion className="h-12 w-12 text-muted-foreground" />
      </div>
      <h2 className="text-xl font-semibold mb-2">{t("common.repository.docNotFound.title")}</h2>
      <p className="text-muted-foreground text-center max-w-md">
        {t("common.repository.docNotFound.description", { slug })}
      </p>
      <p className="text-sm text-muted-foreground mt-4">
        {t("common.repository.docNotFound.hint")}
      </p>
    </div>
  );
}
