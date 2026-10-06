import React, { cache } from "react";
import type { Metadata } from "next";
import { fetchRepoTree, fetchRepoBranches, checkGitHubRepo, fetchProcessingLogs } from "@/lib/repository-api";
import { RepoShell } from "@/components/repo/repo-shell";
import { RepositoryProcessingStatus } from "@/components/repo/repository-processing-status";
import { RepositoryNotFound } from "@/components/repo/repository-not-found";
import { WithAnnouncement } from "@/components/with-announcement";
import { decodeRouteSegment } from "@/lib/repo-route";
import { indexableMetadata, noIndexMetadata, repoCanonicalPath, repoTitle, SITE_DESCRIPTION } from "@/lib/repo-seo";
import RouteProviders from "@/app/route-providers";

// 禁用缓存
export const dynamic = "force-dynamic";

interface RepoLayoutProps {
  children: React.ReactNode;
  params: Promise<{
    owner: string;
    repo: string;
  }>;
}

// React cache：同一次请求中 generateMetadata 与 layout 共享结果，
// 避免爬虫抓一次页面触发两次 tree 查询
const getTreeData = cache(async (owner: string, repo: string) => {
  try {
    const tree = await fetchRepoTree(owner, repo);
    return tree;
  } catch {
    return null;
  }
});

const getBranchesData = cache(async (owner: string, repo: string) => {
  try {
    const branches = await fetchRepoBranches(owner, repo);
    return branches;
  } catch {
    return null;
  }
});

const getGitHubInfo = cache(async (owner: string, repo: string) => {
  try {
    return await checkGitHubRepo(owner, repo);
  } catch {
    return null;
  }
});

const getProcessingStatus = cache(async (owner: string, repo: string) => {
  try {
    const logs = await fetchProcessingLogs(owner, repo, undefined, 1);
    if (logs.statusName === "Pending" || logs.statusName === "Processing" || logs.statusName === "Failed") {
      return logs.statusName;
    }
  } catch {
    return null;
  }

  return null;
});

export async function generateMetadata({ params }: Pick<RepoLayoutProps, "params">): Promise<Metadata> {
  const { owner, repo } = await params;
  const decodedOwner = decodeRouteSegment(owner);
  const decodedRepo = decodeRouteSegment(repo);
  const title = `${repoTitle(decodedOwner, decodedRepo)} Wiki`;
  const description = `AI-generated documentation and code knowledge base for ${repoTitle(decodedOwner, decodedRepo)}.`;
  const canonicalPath = repoCanonicalPath(decodedOwner, decodedRepo);
  const tree = await getTreeData(decodedOwner, decodedRepo);

  if (!tree?.exists || tree.statusName !== "Completed" || tree.nodes.length === 0) {
    return noIndexMetadata(title, description || SITE_DESCRIPTION, canonicalPath);
  }

  return indexableMetadata({
    title,
    description,
    canonicalPath,
  });
}

export default async function RepoLayout({ children, params }: RepoLayoutProps) {
  const { owner, repo } = await params;
  const decodedOwner = decodeRouteSegment(owner);
  const decodedRepo = decodeRouteSegment(repo);
  
  const tree = await getTreeData(decodedOwner, decodedRepo);

  let content: React.ReactNode;

  // API请求失败或仓库不存在时，先查本地处理状态，再检查GitHub
  if (!tree || !tree.exists) {
    const processingStatus = await getProcessingStatus(decodedOwner, decodedRepo);
    if (processingStatus) {
      content = (
        <RepositoryProcessingStatus
          owner={decodedOwner}
          repo={decodedRepo}
          status={processingStatus}
        />
      );
    } else {
      const gitHubInfo = await getGitHubInfo(decodedOwner, decodedRepo);
      content = <RepositoryNotFound owner={decodedOwner} repo={decodedRepo} gitHubInfo={gitHubInfo} />;
    }
  }
  // 仓库正在处理中或等待处理
  else if (tree.statusName === "Pending" || tree.statusName === "Processing" || tree.statusName === "Failed") {
    content = (
      <RepositoryProcessingStatus
        owner={decodedOwner}
        repo={decodedRepo}
        status={tree.statusName}
      />
    );
  }
  // 仓库已完成但没有文档
  else if (tree.nodes.length === 0) {
    content = (
      <RepositoryProcessingStatus
        owner={decodedOwner}
        repo={decodedRepo}
        status="Completed"
      />
    );
  }
  else {
    // 获取分支和语言数据
    const branches = await getBranchesData(decodedOwner, decodedRepo);

    return (
      <RepoShell
        owner={decodedOwner}
        repo={decodedRepo}
        initialNodes={tree.nodes}
        initialBranches={branches ?? undefined}
        initialBranch={tree.currentBranch}
        initialLanguage={tree.currentLanguage}
        initialHasGraphifyArtifact={tree.hasGraphifyArtifact}
      >
        {children}
      </RepoShell>
    );
  }

  // For non-ready states, wrap content in RouteProviders
  return (
    <RouteProviders>
      <WithAnnouncement>{content}</WithAnnouncement>
    </RouteProviders>
  );
}
