import type { MetadataRoute } from "next";
import { fetchRepoTree, fetchRepositoryList } from "@/lib/repository-api";
import type { RepoTreeNode, RepositoryItemResponse } from "@/types/repository";
import { buildRepoBasePath, buildRepoDocPath } from "@/lib/repo-route";
import { absoluteUrl } from "@/lib/repo-seo";

// ISR 全路由缓存：/sitemap.xml 响应按 revalidate 周期复用，
// 爬虫高频抓取不再触发后端全量遍历；进程内缓存继续做构建单飞
export const revalidate = 3600;

const PAGE_SIZE = 100;
const MAX_REPOSITORIES = 1000;
const MAX_URLS = 50000;
const TREE_CONCURRENCY = 4;
const DEFAULT_SITEMAP_REVALIDATE_SECONDS = 3600;

type SitemapCacheEntry = {
  urls: MetadataRoute.Sitemap;
  expiresAt: number;
};

type RepoSitemapSource = {
  owner: string;
  repo: string;
  lastModified: Date;
  nodes: RepoTreeNode[];
};

let sitemapCache: SitemapCacheEntry | null = null;
let sitemapRefresh: Promise<MetadataRoute.Sitemap> | null = null;

function getSitemapRevalidateMs(): number {
  const parsed = Number.parseInt(process.env.SITEMAP_REVALIDATE_SECONDS ?? "", 10);
  const seconds = Number.isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_SITEMAP_REVALIDATE_SECONDS;
  return seconds * 1000;
}

async function mapPool<T, R>(items: T[], concurrency: number, mapper: (item: T) => Promise<R>): Promise<R[]> {
  if (items.length === 0) {
    return [];
  }

  const results: R[] = new Array(items.length);
  let nextIndex = 0;

  const worker = async () => {
    while (nextIndex < items.length) {
      const current = nextIndex;
      nextIndex += 1;
      results[current] = await mapper(items[current]);
    }
  };

  await Promise.all(Array.from({ length: Math.min(concurrency, items.length) }, () => worker()));
  return results;
}

function collectLeafSlugs(nodes: RepoTreeNode[]): string[] {
  const slugs: string[] = [];

  const walk = (items: RepoTreeNode[]) => {
    for (const item of items) {
      if (item.children.length === 0) {
        slugs.push(item.slug);
        continue;
      }

      walk(item.children);
    }
  };

  walk(nodes);
  return slugs;
}

function escapeXml(value: string): string {
  return value.replace(/[&<>]/g, (character) => {
    switch (character) {
      case "&":
        return "&amp;";
      case "<":
        return "&lt;";
      case ">":
        return "&gt;";
      default:
        return character;
    }
  });
}

function addSitemapUrl(
  urls: MetadataRoute.Sitemap,
  knownUrls: Set<string>,
  path: string,
  entry: Omit<MetadataRoute.Sitemap[number], "url">,
) {
  if (urls.length >= MAX_URLS) {
    return;
  }

  const url = absoluteUrl(path);
  if (knownUrls.has(url)) {
    return;
  }

  knownUrls.add(url);
  urls.push({ url: escapeXml(url), ...entry });
}

function addTreeUrls(
  urls: MetadataRoute.Sitemap,
  knownUrls: Set<string>,
  owner: string,
  repo: string,
  nodes: RepoTreeNode[],
  lastModified: Date,
) {
  for (const slug of collectLeafSlugs(nodes)) {
    if (urls.length >= MAX_URLS) {
      return;
    }

    addSitemapUrl(urls, knownUrls, buildRepoDocPath(owner, repo, slug), {
      lastModified,
      changeFrequency: "weekly",
      priority: 0.6,
    });
  }
}

async function loadRepoSitemapSource(repo: RepositoryItemResponse): Promise<RepoSitemapSource | null> {
  try {
    const tree = await fetchRepoTree(repo.orgName, repo.repoName, undefined, undefined, true);
    if (!tree.exists || tree.statusName !== "Completed" || tree.nodes.length === 0) {
      return null;
    }

    return {
      owner: repo.orgName,
      repo: repo.repoName,
      lastModified: new Date(repo.updatedAt ?? repo.createdAt),
      nodes: tree.nodes,
    };
  } catch {
    return null;
  }
}

async function buildSitemapUrls(): Promise<{ urls: MetadataRoute.Sitemap; complete: boolean }> {
  const urls: MetadataRoute.Sitemap = [];
  const knownUrls = new Set<string>();

  addSitemapUrl(urls, knownUrls, "/", {
    changeFrequency: "daily",
    priority: 1,
  });

  try {
    let page = 1;
    let processedRepositories = 0;
    let total = Number.POSITIVE_INFINITY;

    while (processedRepositories < Math.min(total, MAX_REPOSITORIES) && urls.length < MAX_URLS) {
      const response = await fetchRepositoryList({
        page,
        pageSize: PAGE_SIZE,
        isPublic: true,
        status: 2,
        sortBy: "updatedAt",
      }, true);

      total = response.total;
      if (response.items.length === 0) {
        break;
      }

      const remaining = Math.min(total, MAX_REPOSITORIES) - processedRepositories;
      const pageItems = response.items.slice(0, Math.max(remaining, 0));
      processedRepositories += pageItems.length;

      const sources = await mapPool(pageItems, TREE_CONCURRENCY, loadRepoSitemapSource);

      for (const source of sources) {
        if (!source || urls.length >= MAX_URLS) {
          continue;
        }

        addSitemapUrl(urls, knownUrls, buildRepoBasePath(source.owner, source.repo), {
          lastModified: source.lastModified,
          changeFrequency: "weekly",
          priority: 0.7,
        });
        addTreeUrls(urls, knownUrls, source.owner, source.repo, source.nodes, source.lastModified);
      }

      page += 1;
    }
  } catch {
    return { urls, complete: false };
  }

  return { urls, complete: true };
}

async function refreshSitemap(): Promise<MetadataRoute.Sitemap> {
  if (sitemapRefresh) {
    return sitemapRefresh;
  }

  sitemapRefresh = buildSitemapUrls()
    .then((result) => {
      if (result.complete) {
        sitemapCache = {
          urls: result.urls,
          expiresAt: Date.now() + getSitemapRevalidateMs(),
        };
      }

      return sitemapCache?.urls ?? result.urls;
    })
    .catch(() => sitemapCache?.urls ?? [])
    .finally(() => {
      sitemapRefresh = null;
    });

  return sitemapRefresh;
}

async function getCachedSitemap(): Promise<MetadataRoute.Sitemap> {
  const cached = sitemapCache;
  if (cached && cached.expiresAt > Date.now()) {
    return cached.urls;
  }

  if (cached) {
    void refreshSitemap();
    return cached.urls;
  }

  return refreshSitemap();
}

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  return getCachedSitemap();
}
