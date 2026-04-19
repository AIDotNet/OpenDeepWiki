"use client";

import React, { useEffect, useState } from "react";
import { useSearchParams, usePathname } from "next/navigation";
import Link from "next/link";
import type { RepoTreeNode, RepoBranchesResponse } from "@/types/repository";
import { BranchLanguageSelector } from "./branch-language-selector";
import { fetchRepoTree, fetchRepoBranches } from "@/lib/repository-api";
import { Network, Download } from "lucide-react";
import { ChatAssistant, buildCatalogMenu } from "@/components/chat";
import { buildRepoBasePath, buildRepoMindMapPath } from "@/lib/repo-route";
import { DocsLayout } from "fumadocs-ui/layouts/docs";
import { RootProvider } from "fumadocs-ui/provider/next";
import { convertToPageTree } from "@/lib/page-tree-utils";

const repoUiText = {
  zh: {
    wikiTitle: "仓库 Wiki",
    mindMap: "项目架构",
    exportDocs: "导出文档",
    exporting: "导出中...",
  },
  en: {
    wikiTitle: "Repository Wiki",
    mindMap: "Project Architecture",
    exportDocs: "Export Docs",
    exporting: "Exporting...",
  },
} as const;

interface RepoShellProps {
  owner: string;
  repo: string;
  initialNodes: RepoTreeNode[];
  children: React.ReactNode;
  initialBranches?: RepoBranchesResponse;
  initialBranch?: string;
  initialLanguage?: string;
  uiLocale?: "zh" | "en";
}

export function RepoShell({ 
  owner, 
  repo, 
  initialNodes, 
  children,
  initialBranches,
  initialBranch,
  initialLanguage,
  uiLocale = "zh",
}: RepoShellProps) {
  const searchParams = useSearchParams();
  const pathname = usePathname();
  const urlBranch = searchParams.get("branch");
  const urlLang = searchParams.get("lang");
  const repoBasePath = buildRepoBasePath(owner, repo);
  
  const [nodes, setNodes] = useState<RepoTreeNode[]>(initialNodes);
  const [branches, setBranches] = useState<RepoBranchesResponse | undefined>(initialBranches);
  const [currentBranch, setCurrentBranch] = useState(initialBranch || "");
  const [currentLanguage, setCurrentLanguage] = useState(initialLanguage || "");
  const [isLoading, setIsLoading] = useState(false);
  const [isExporting, setIsExporting] = useState(false);
  const copy = repoUiText[uiLocale];

  const currentDocPath = React.useMemo(() => {
    const encodedPrefix = `${repoBasePath}/`;
    if (pathname.startsWith(encodedPrefix)) {
      return pathname.slice(encodedPrefix.length);
    }

    const rawPrefix = `/${owner}/${repo}/`;
    if (pathname.startsWith(rawPrefix)) {
      return pathname.slice(rawPrefix.length);
    }
    return "";
  }, [pathname, owner, repo, repoBasePath]);

  useEffect(() => {
    const branch = urlBranch || undefined;
    const lang = urlLang || undefined;
    
    if (!branch && !lang) {
      return;
    }

    if (branch === currentBranch && lang === currentLanguage) {
      return;
    }

    const fetchData = async () => {
      setIsLoading(true);
      try {
        const [treeData, branchesData] = await Promise.all([
          fetchRepoTree(owner, repo, branch, lang),
          fetchRepoBranches(owner, repo),
        ]);
        
        if (treeData.nodes.length > 0) {
          setNodes(treeData.nodes);
          setCurrentBranch(treeData.currentBranch || "");
          setCurrentLanguage(treeData.currentLanguage || "");
        }
        if (branchesData) {
          setBranches(branchesData);
        }
      } catch (error) {
        console.error("Failed to fetch tree data:", error);
      } finally {
        setIsLoading(false);
      }
    };

    fetchData();
  }, [urlBranch, urlLang, owner, repo, currentBranch, currentLanguage]);

  const queryString = searchParams.toString();

  const mindMapUrl = queryString 
    ? `${buildRepoMindMapPath(owner, repo)}?${queryString}`
    : buildRepoMindMapPath(owner, repo);

  const handleExport = async () => {
    if (isExporting) return;
    
    setIsExporting(true);
    try {
      const params = new URLSearchParams();
      if (currentBranch) params.set("branch", currentBranch);
      if (currentLanguage) params.set("lang", currentLanguage);
      
      const exportUrl = `/api/v1/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/export${params.toString() ? `?${params.toString()}` : ""}`;
      
      const response = await fetch(exportUrl);
      if (!response.ok) {
        throw new Error(copy.exportDocs);
      }
      
      const contentDisposition = response.headers.get("content-disposition");
      let fileName = `${owner}-${repo}-${currentBranch || "main"}-${currentLanguage || "zh"}.zip`;
      if (contentDisposition) {
        const fileNameMatch = contentDisposition.match(/filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/);
        if (fileNameMatch?.[1]) {
          fileName = fileNameMatch[1].replace(/['"]/g, "");
        }
      }
      
      const blob = await response.blob();
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = fileName;
      document.body.appendChild(a);
      a.click();
      window.URL.revokeObjectURL(url);
      document.body.removeChild(a);
    } catch (error) {
      console.error("Export failed:", error);
    } finally {
      setIsExporting(false);
    }
  };

  const title = `${owner}/${repo}`;

  const pageTree = convertToPageTree(nodes, owner, repo, queryString);

  const sidebarBanner = (
    <div className="space-y-3 p-4">
      {branches && (
        <BranchLanguageSelector
          owner={owner}
          repo={repo}
          branches={branches}
          currentBranch={currentBranch}
          currentLanguage={currentLanguage}
        />
      )}
      <div className="space-y-2">
        <Link
          href={mindMapUrl}
          className="flex items-center gap-2 px-3 py-2 rounded-lg bg-blue-500/10 border border-blue-500/30 text-blue-700 dark:text-blue-300 hover:bg-blue-500/20 transition-colors"
        >
          <Network className="h-4 w-4" />
          <span className="font-medium text-sm">{copy.mindMap}</span>
        </Link>
        <button
          onClick={handleExport}
          disabled={isExporting}
          className="flex items-center gap-2 px-3 py-2 rounded-lg bg-green-500/10 border border-green-500/30 text-green-700 dark:text-green-300 hover:bg-green-500/20 transition-colors disabled:opacity-50 disabled:cursor-not-allowed w-full"
        >
          <Download className="h-4 w-4" />
          <span className="font-medium text-sm">
            {isExporting ? copy.exporting : copy.exportDocs}
          </span>
        </button>
      </div>
    </div>
  );

  return (
    <RootProvider>
      <DocsLayout
        tree={pageTree}
        nav={{
          title,
          enabled: true,
        }}
        sidebar={{
          enabled: true,
          banner: sidebarBanner,
        }}
      >
        {isLoading ? (
          <div className="flex items-center justify-center py-20">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary"></div>
          </div>
        ) : (
          children
        )}
      </DocsLayout>

      <ChatAssistant
        context={{
          owner,
          repo,
          branch: currentBranch,
          language: currentLanguage,
          currentDocPath,
          catalogMenu: buildCatalogMenu(nodes),
        }}
      />
    </RootProvider>
  );
}