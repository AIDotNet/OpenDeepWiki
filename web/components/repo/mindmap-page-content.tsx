"use client";

import { Network, Loader2, AlertCircle } from "lucide-react";
import { MindMapViewer } from "@/components/repo/mind-map-viewer";
import { useTranslations } from "@/hooks/use-translations";

function MindMapState({
  icon,
  title,
  description,
}: {
  icon: React.ReactNode;
  title: string;
  description: string;
}) {
  return (
    <div className="flex flex-col items-center justify-center py-20 text-center">
      {icon}
      <h2 className="mb-2 text-xl font-semibold">{title}</h2>
      <p className="text-muted-foreground">{description}</p>
    </div>
  );
}

interface MindMapData {
  content?: string | null;
  statusName?: string;
  branch?: string;
}

interface MindMapPageContentProps {
  owner: string;
  repo: string;
  mindMap: MindMapData | null;
}

export function MindMapPageContent({ owner, repo, mindMap }: MindMapPageContentProps) {
  const t = useTranslations();

  // 思维导图不存在
  if (!mindMap) {
    return (
      <MindMapState
        icon={<AlertCircle className="mb-4 h-12 w-12 text-muted-foreground" />}
        title={t("mindmap.error")}
        description={t("mindmap.errorDescription")}
      />
    );
  }

  // 思维导图正在生成中
  if (mindMap.statusName === "Pending" || mindMap.statusName === "Processing") {
    return (
      <MindMapState
        icon={<Loader2 className="mb-4 h-12 w-12 animate-spin text-blue-500" />}
        title={t("mindmap.loading")}
        description={t("mindmap.loadingDescription")}
      />
    );
  }

  // 思维导图生成失败
  if (mindMap.statusName === "Failed") {
    return (
      <MindMapState
        icon={<AlertCircle className="mb-4 h-12 w-12 text-red-500" />}
        title={t("mindmap.failed")}
        description={t("mindmap.failedDescription")}
      />
    );
  }

  // 思维导图内容为空
  if (!mindMap.content) {
    return (
      <MindMapState
        icon={<Network className="mb-4 h-12 w-12 text-muted-foreground" />}
        title={t("mindmap.empty")}
        description={t("mindmap.emptyDescription")}
      />
    );
  }

  return (
    <div>
      <div className="mb-6">
        <div className="mb-3 flex items-center gap-3">
          <Network className="h-7 w-7 text-blue-500" />
          <h1 className="text-2xl font-bold">{t("mindmap.title")}</h1>
        </div>
        <p className="text-sm text-muted-foreground">
          {t("mindmap.description", { owner, repo })}
        </p>
      </div>

      <div className="-mx-4 md:-mx-6 lg:-mx-8">
        <MindMapViewer
          content={mindMap.content}
          owner={owner}
          repo={repo}
          branch={mindMap.branch}
        />
      </div>
    </div>
  );
}
