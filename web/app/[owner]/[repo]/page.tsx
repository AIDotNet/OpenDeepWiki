import { redirect } from "next/navigation";
import { cache } from "react";
import { fetchRepoTree } from "@/lib/repository-api";
import { DocNotFound } from "@/components/repo/doc-not-found";
import { buildRepoDocPath, decodeRouteSegment } from "@/lib/repo-route";

interface RepoIndexProps {
  params: Promise<{
    owner: string;
    repo: string;
  }>;
  searchParams: Promise<{
    branch?: string;
    lang?: string;
  }>;
}

// React cache：同一次请求内去重；URL 与 layout 一致时再由 ssr-cache 合并
const getTreeData = cache(async (owner: string, repo: string, branch?: string, lang?: string) => {
  try {
    return await fetchRepoTree(owner, repo, branch, lang);
  } catch {
    return null;
  }
});

export default async function RepoIndex({ params, searchParams }: RepoIndexProps) {
  const { owner, repo } = await params;
  const decodedOwner = decodeRouteSegment(owner);
  const decodedRepo = decodeRouteSegment(repo);
  const resolvedSearchParams = await searchParams;
  const branch = resolvedSearchParams?.branch;
  const lang = resolvedSearchParams?.lang;
  
  const tree = await getTreeData(decodedOwner, decodedRepo, branch, lang);
  
  // API错误，layout会处理
  if (!tree) {
    return null;
  }
  
  // 仓库不存在，layout会处理
  if (!tree.exists) {
    return null;
  }

  // 仓库正在处理中、等待处理或失败，layout会处理显示
  if (tree.statusName !== "Completed") {
    return null;
  }

  // 有默认文档，重定向
  if (tree.defaultSlug) {
    const params = new URLSearchParams();
    if (branch) params.set("branch", branch);
    if (lang) params.set("lang", lang);
    const query = params.toString();

    redirect(`${buildRepoDocPath(decodedOwner, decodedRepo, tree.defaultSlug)}${query ? `?${query}` : ""}`);
  }

  // 没有默认文档但有目录，显示提示
  if (tree.nodes.length > 0) {
    return (
      <div className="mx-auto max-w-4xl">
        <DocNotFound slug="" />
      </div>
    );
  }

  // 空仓库，layout会处理
  return null;
}
