import type * as PageTree from "fumadocs-core/page-tree";
import type { RepoTreeNode } from "@/types/repository";
import { buildRepoDocPath } from "./repo-route";

export function convertToPageTree(
  nodes: RepoTreeNode[],
  owner: string,
  repo: string,
  queryString?: string
): PageTree.Root {
  const children: PageTree.Node[] = nodes.map((node) =>
    convertNodeToPageTreeItem(node, owner, repo, queryString)
  );

  return {
    name: `${owner}/${repo}`,
    children,
  };
}

function convertNodeToPageTreeItem(
  node: RepoTreeNode,
  owner: string,
  repo: string,
  queryString?: string
): PageTree.Node {
  const url = queryString
    ? `${buildRepoDocPath(owner, repo, node.slug)}?${queryString}`
    : buildRepoDocPath(owner, repo, node.slug);

  if (node.children && node.children.length > 0) {
    const childItems: PageTree.Node[] = node.children.map((child) =>
      convertNodeToPageTreeItem(child, owner, repo, queryString)
    );

    return {
      type: "folder",
      name: node.title,
      index: {
        type: "page",
        name: node.title,
        url,
      },
      children: childItems,
    };
  }

  return {
    type: "page",
    name: node.title,
    url,
  };
}