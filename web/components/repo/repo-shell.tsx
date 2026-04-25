"use client";

import React, { useEffect, useState } from "react";
import { useSearchParams, usePathname } from "next/navigation";
import Link from "next/link";
import type { RepoTreeNode, RepoBranchesResponse } from "@/types/repository";
import { BranchLanguageSelector } from "./branch-language-selector";
import { fetchRepoTree, fetchRepoBranches } from "@/lib/repository-api";
import { ChevronDown, ChevronRight, Network, Download } from "lucide-react";
import { ChatAssistant, buildCatalogMenu } from "@/components/chat";
import { buildRepoBasePath, buildRepoDocPath, buildRepoMindMapPath } from "@/lib/repo-route";

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

function SidebarTree({
  nodes,
  owner,
  repo,
  queryString,
  currentPath,
  depth = 0,
}: {
  nodes: RepoTreeNode[];
  owner: string;
  repo: string;
  queryString: string;
  currentPath: string;
  depth?: number;
}) {
  // 追踪每个目录节点的展开/折叠状态
  const [expandedSlugs, setExpandedSlugs] = useState<Set<string>>(() => {
    const expanded = new Set<string>();
    const expandParents = (items: RepoTreeNode[], targetPath: string): boolean => {
      for (const item of items) {
        if (item.slug === targetPath) return true;
        if (item.children && item.children.length > 0 && expandParents(item.children, targetPath)) {
          expanded.add(item.slug);
          return true;
        }
      }
      return false;
    };
    if (currentPath) expandParents(nodes, currentPath);
    return expanded;
  });

  const toggleExpand = (slug: string) => {
    setExpandedSlugs((prev) => {
      const next = new Set(prev);
      if (next.has(slug)) {
        next.delete(slug);
      } else {
        next.add(slug);
      }
      return next;
    });
  };

  return (
    <ul className={depth === 0 ? "space-y-1" : "mt-1 space-y-1 border-l border-border/60 pl-3"}>
      {nodes.map((node) => {
        const hasChildren = node.children && node.children.length > 0;
        const isDirectory = hasChildren;
        const isExpanded = expandedSlugs.has(node.slug);
        const isActive = currentPath === node.slug;
        const href = queryString
          ? `${buildRepoDocPath(owner, repo, node.slug)}?${queryString}`
          : buildRepoDocPath(owner, repo, node.slug);

        return (
          <li key={node.slug}>
            <div className="flex items-center">
              {/* 目录节点显示展开/折叠箭头 */}
              {isDirectory && (
                <button
                  type="button"
                  onClick={() => toggleExpand(node.slug)}
                  className="flex h-6 w-4 shrink-0 items-center justify-center hover:text-foreground"
                >
                  {isExpanded ? (
                    <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" />
                  ) : (
                    <ChevronRight className="h-3.5 w-3.5 text-muted-foreground" />
                  )}
                </button>
              )}
              {/* 节点标题 */}
              {isDirectory ? (
                <button
                  type="button"
                  onClick={() => toggleExpand(node.slug)}
                  className={[
                    "flex-1 rounded-md px-3 py-2 text-left text-sm transition-colors",
                    isActive
                      ? "bg-primary text-primary-foreground font-medium"
                      : "text-foreground/80 hover:bg-muted hover:text-foreground",
                  ].join(" ")}
                >
                  {node.title}
                </button>
              ) : (
                <Link
                  href={href}
                  className={[
                    "block flex-1 rounded-md px-3 py-2 text-sm transition-colors",
                    isActive
                      ? "bg-primary text-primary-foreground"
                      : "text-foreground/80 hover:bg-muted hover:text-foreground",
                  ].join(" ")}
                >
                  {node.title}
                </Link>
              )}
            </div>
            {isDirectory && isExpanded && (
              <SidebarTree
                nodes={node.children}
                owner={owner}
                repo={repo}
                queryString={queryString}
                currentPath={currentPath}
                depth={depth + 1}
              />
            )}
          </li>
        );
      })}
    </ul>
  );
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

  // 从pathname提取当前文档路径
  const currentDocPath = React.useMemo(() => {
    // pathname格式: /owner/repo/slug 或 /owner/repo/path/to/doc
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

  // 当 URL 参数变化时，重新获取数据
  useEffect(() => {
    const branch = urlBranch || undefined;
    const lang = urlLang || undefined;
    
    // 如果没有指定参数，使用初始值
    if (!branch && !lang) {
      return;
    }

    // 如果参数和当前状态相同，不需要重新获取
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

  // 构建查询字符串 - 优先使用 URL 参数，确保链接始终保持当前 URL 的参数
  const queryString = searchParams.toString();

  // 构建思维导图链接
  const mindMapUrl = queryString 
    ? `${buildRepoMindMapPath(owner, repo)}?${queryString}`
    : buildRepoMindMapPath(owner, repo);

  // 导出功能处理
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
      
      // 获取文件名
      const contentDisposition = response.headers.get("content-disposition");
      let fileName = `${owner}-${repo}-${currentBranch || "main"}-${currentLanguage || "zh"}.zip`;
      if (contentDisposition) {
        const fileNameMatch = contentDisposition.match(/filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/);
        if (fileNameMatch?.[1]) {
          fileName = fileNameMatch[1].replace(/['"]/g, "");
        }
      }
      
      // 获取原始字节数据
      const rawBytes = await response.arrayBuffer();

      // MiniApi 框架会将 FileContentResult 序列化为 JSON（包含 base64 编码的 fileContents 字段）
      // 需要检测并解码，否则直接使用原始数据
      let blob: Blob;
      const textDecoder = new TextDecoder();
      const textPreview = textDecoder.decode(rawBytes.slice(0, 50));

      if (textPreview.startsWith('{"') && textPreview.includes('fileContents')) {
        const jsonString = textDecoder.decode(rawBytes);
        const json = JSON.parse(jsonString);
        const base64Content = json.fileContents as string;
        const binaryString = atob(base64Content);
        const byteArray = new Uint8Array(binaryString.length);
        for (let i = 0; i < binaryString.length; i++) {
          byteArray[i] = binaryString.charCodeAt(i);
        }
        blob = new Blob([byteArray], { type: "application/zip" });
      } else {
        blob = new Blob([rawBytes], { type: "application/zip" });
      }

      // 下载文件
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
      // 可以在这里添加错误提示
    } finally {
      setIsExporting(false);
    }
  };

  const title = `${owner}/${repo}`;

  // 构建侧边栏顶部的选择器和操作按钮
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
    <div className="min-h-screen bg-background">
      <div className="border-b border-border/70 bg-background/95 backdrop-blur">
        <div className="mx-auto flex max-w-7xl items-center justify-between gap-4 px-6 py-4">
          <div>
            <div className="text-xs uppercase tracking-[0.2em] text-muted-foreground">{copy.wikiTitle}</div>
            <div className="text-lg font-semibold">{title}</div>
          </div>
        </div>
      </div>

      <div className="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-6 lg:flex-row lg:px-6">
        <aside className="w-full shrink-0 lg:sticky lg:top-6 lg:w-80 lg:self-start">
          <div className="rounded-xl border border-border/70 bg-card p-4 shadow-sm">
            {sidebarBanner}
            <div className="mt-4 border-t border-border/70 pt-4">
              <SidebarTree
                nodes={nodes}
                owner={owner}
                repo={repo}
                queryString={queryString}
                currentPath={currentDocPath}
              />
            </div>
          </div>
        </aside>

        <main className="min-w-0 flex-1">
          <div className="rounded-xl border border-border/70 bg-card p-4 shadow-sm sm:p-6">
            {isLoading ? (
              <div className="flex items-center justify-center py-20">
                <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary"></div>
              </div>
            ) : (
              children
            )}
          </div>
        </main>
      </div>

      {/* 文档对话助手悬浮球 */}
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
    </div>
  );
}
