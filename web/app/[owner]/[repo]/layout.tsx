import React from "react";
import { fetchRepoTree, fetchRepoBranches, checkGitHubRepo } from "@/lib/repository-api";
import { RepoShell } from "@/components/repo/repo-shell";
import { RepositoryProcessingStatus } from "@/components/repo/repository-processing-status";
import { RepositoryNotFound } from "@/components/repo/repository-not-found";
import { RootProvider } from "fumadocs-ui/provider/next";
import { decodeRouteSegment } from "@/lib/repo-route";
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

async function getTreeData(owner: string, repo: string) {
  try {
    const tree = await fetchRepoTree(owner, repo);
    return tree;
  } catch {
    return null;
  }
}

async function getBranchesData(owner: string, repo: string) {
  try {
    const branches = await fetchRepoBranches(owner, repo);
    return branches;
  } catch {
    return null;
  }
}

async function getGitHubInfo(owner: string, repo: string) {
  try {
    return await checkGitHubRepo(owner, repo);
  } catch {
    return null;
  }
}

export default async function RepoLayout({ children, params }: RepoLayoutProps) {
  const { owner, repo } = await params;
  const decodedOwner = decodeRouteSegment(owner);
  const decodedRepo = decodeRouteSegment(repo);
  
  const tree = await getTreeData(decodedOwner, decodedRepo);

  let content: React.ReactNode;

  // API请求失败或仓库不存在，检查GitHub
  if (!tree || !tree.exists) {
    const gitHubInfo = await getGitHubInfo(decodedOwner, decodedRepo);
    content = <RepositoryNotFound owner={decodedOwner} repo={decodedRepo} gitHubInfo={gitHubInfo} />;
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
      <RootProvider>
        <RouteProviders>
          <RepoShell 
            owner={decodedOwner} 
            repo={decodedRepo} 
            initialNodes={tree.nodes}
            initialBranches={branches ?? undefined}
            initialBranch={tree.currentBranch}
            initialLanguage={tree.currentLanguage}
          >
            {children}
          </RepoShell>
        </RouteProviders>
      </RootProvider>
    );
  }

  return <RouteProviders>{content}</RouteProviders>;
}
