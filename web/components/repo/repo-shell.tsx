"use client";

import React, { useEffect, useState } from "react";
import { useSearchParams, usePathname } from "next/navigation";
import Link from "next/link";
import { DocsLayout } from "fumadocs-ui/layouts/docs";
import type * as PageTree from "fumadocs-core/page-tree";
import type { RepoTreeNode, RepoBranchesResponse } from "@/types/repository";
import { BranchLanguageSelector } from "./branch-language-selector";
import { fetchRepoTree, fetchRepoBranches } from "@/lib/repository-api";
import { Network, Download } from "lucide-react";
import { ChatAssistant, buildCatalogMenu } from "@/components/chat";
import { useTranslations } from "@/hooks/use-translations";

interface RepoShellProps {
  owner: string;
  repo: string;
  initialNodes: RepoTreeNode[];
  children: React.ReactNode;
  initialBranches?: RepoBranchesResponse;
  initialBranch?: string;
  initialLanguage?: string;
}

/**
 * Convert RepoTreeNode to fumadocs PageTree.Node
 */
function convertToPageTreeNode(
  node: RepoTreeNode,
  owner: string,
  repo: string,
  queryString: string
): PageTree.Node {
  const baseUrl = `/${owner}/${repo}/${node.slug}`;
  // Links need query params to preserve branch and lang state
  const url = queryString ? `${baseUrl}?${queryString}` : baseUrl;

  if (node.children && node.children.length > 0) {
    return {
      type: "folder",
      name: node.title,
      url,
      children: node.children.map((child) =>
        convertToPageTreeNode(child, owner, repo, queryString)
      ),
    } as PageTree.Folder;
  }

  return {
    type: "page",
    name: node.title,
    url,
  } as PageTree.Item;
}

/**
 * Convert RepoTreeNode[] to fumadocs PageTree.Root
 */
function convertToPageTree(
  nodes: RepoTreeNode[],
  owner: string,
  repo: string,
  queryString: string
): PageTree.Root {
  return {
    name: `${owner}/${repo}`,
    children: nodes.map((node) => convertToPageTreeNode(node, owner, repo, queryString)),
  };
}

export function RepoShell({ 
  owner, 
  repo, 
  initialNodes, 
  children,
  initialBranches,
  initialBranch,
  initialLanguage,
}: RepoShellProps) {
  const searchParams = useSearchParams();
  const pathname = usePathname();
  const urlBranch = searchParams.get("branch");
  const urlLang = searchParams.get("lang");
  const t = useTranslations();
  
  const [nodes, setNodes] = useState<RepoTreeNode[]>(initialNodes);
  const [branches, setBranches] = useState<RepoBranchesResponse | undefined>(initialBranches);
  const [currentBranch, setCurrentBranch] = useState(initialBranch || "");
  const [currentLanguage, setCurrentLanguage] = useState(initialLanguage || "");
  const [isLoading, setIsLoading] = useState(false);
  const [isExporting, setIsExporting] = useState(false);

  // Extract current document path from pathname
  const currentDocPath = React.useMemo(() => {
    // pathname format: /owner/repo/slug or /owner/repo/path/to/doc
    const prefix = `/${owner}/${repo}/`;
    if (pathname.startsWith(prefix)) {
      return pathname.slice(prefix.length);
    }
    return "";
  }, [pathname, owner, repo]);

  // Re-fetch data when URL params change
  useEffect(() => {
    const branch = urlBranch || undefined;
    const lang = urlLang || undefined;
    
    // If no params specified, use initial values
    if (!branch && !lang) {
      return;
    }

    // No need to re-fetch if params are the same as current state
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

  // Build query string - prioritize URL params, ensure links always preserve current URL params
  const queryString = searchParams.toString();

  // Build mind map link
  const mindMapUrl = queryString 
    ? `/${owner}/${repo}/mindmap?${queryString}` 
    : `/${owner}/${repo}/mindmap`;

  // Export handler
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
        throw new Error("Export failed");
      }
      
      // Get filename
      const contentDisposition = response.headers.get("content-disposition");
      let fileName = `${owner}-${repo}-${currentBranch || "main"}-${currentLanguage || "zh"}.zip`;
      if (contentDisposition) {
        const fileNameMatch = contentDisposition.match(/filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/);
        if (fileNameMatch?.[1]) {
          fileName = fileNameMatch[1].replace(/['"]/g, "");
        }
      }
      
      // Download file
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
      // Error notification can be added here
    } finally {
      setIsExporting(false);
    }
  };

  const tree = convertToPageTree(nodes, owner, repo, queryString);
  const title = `${owner}/${repo}`;

  // Build sidebar top selector and action buttons
  const sidebarBanner = (
    <div className="space-y-3">
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
          <span className="font-medium text-sm">{t("mindmap.title")}</span>
        </Link>
        <button
          onClick={handleExport}
          disabled={isExporting}
          className="flex items-center gap-2 px-3 py-2 rounded-lg bg-green-500/10 border border-green-500/30 text-green-700 dark:text-green-300 hover:bg-green-500/20 transition-colors disabled:opacity-50 disabled:cursor-not-allowed w-full"
        >
          <Download className="h-4 w-4" />
          <span className="font-medium text-sm">
            {isExporting ? "Exporting..." : "Export document"}
          </span>
        </button>
      </div>
    </div>
  );

  return (
    <DocsLayout
      tree={tree}
      nav={{
        title: "KeboolaDeepWiki",
        url: "/",
      }}
      sidebar={{
        defaultOpenLevel: 1,
        collapsible: true,
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
      
      {/* Document chat assistant floating button */}
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
    </DocsLayout>
  );
}
