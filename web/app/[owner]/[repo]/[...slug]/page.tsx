import type { Metadata } from "next";
import { notFound, redirect } from "next/navigation";
import { cache } from "react";
import { fetchRepoDoc, fetchRepoTree } from "@/lib/repository-api";
import { extractHeadings } from "@/lib/markdown";
import { MarkdownRenderer } from "@/components/repo/markdown-renderer";
import { SourceFiles } from "@/components/repo/source-files";
import { buildRepoDocPath, decodeRouteSegment } from "@/lib/repo-route";
import type { RepoTreeNode } from "@/types/repository";
import {
  createMarkdownDescription,
  createTechArticleJsonLd,
  docCanonicalPath,
  extractMarkdownTitle,
  indexableMetadata,
  noIndexMetadata,
  repoTitle,
  safeJsonLd,
} from "@/lib/repo-seo";
import { getLocale, getTranslations } from "next-intl/server";

interface RepoDocPageProps {
  params: Promise<{
    owner: string;
    repo: string;
    slug: string[];
  }>;
  searchParams: Promise<{
    branch?: string;
    lang?: string;
  }>;
}

// React cache：同一次请求中 generateMetadata 与页面共享文档数据，
// 避免爬虫抓一次页面触发两次 doc 查询
const getDocData = cache(async (owner: string, repo: string, slug: string, branch?: string, lang?: string) => {
  try {
    const doc = await fetchRepoDoc(owner, repo, slug, branch, lang);
    if (!doc.exists) {
      return null;
    }
    const headings = extractHeadings(doc.content, 3);
    return { doc, headings };
  } catch {
    return null;
  }
});

function findNodeBySlug(nodes: RepoTreeNode[], slug: string): RepoTreeNode | null {
  for (const node of nodes) {
    if (node.slug === slug) {
      return node;
    }

    const match = findNodeBySlug(node.children ?? [], slug);
    if (match) {
      return match;
    }
  }

  return null;
}

function findFirstLeafSlug(node: RepoTreeNode): string | null {
  const children = node.children ?? [];
  if (children.length === 0) {
    return node.slug;
  }

  for (const child of children) {
    const slug = findFirstLeafSlug(child);
    if (slug) {
      return slug;
    }
  }

  return null;
}

async function getDirectoryRedirectSlug(owner: string, repo: string, slug: string, branch?: string, lang?: string) {
  try {
    const tree = await fetchRepoTree(owner, repo, branch, lang);
    const node = findNodeBySlug(tree.nodes, slug);
    if (!node || (node.children ?? []).length === 0) {
      return null;
    }

    const leafSlug = findFirstLeafSlug(node);
    return leafSlug && leafSlug !== slug ? leafSlug : null;
  } catch {
    return null;
  }
}

function buildQueryString(branch?: string, lang?: string) {
  const params = new URLSearchParams();
  if (branch) params.set("branch", branch);
  if (lang) params.set("lang", lang);
  const query = params.toString();
  return query ? `?${query}` : "";
}

export async function generateMetadata({ params, searchParams }: RepoDocPageProps): Promise<Metadata> {
  const { owner, repo, slug: slugParts } = await params;
  const decodedOwner = decodeRouteSegment(owner);
  const decodedRepo = decodeRouteSegment(repo);
  const resolvedSearchParams = await searchParams;
  const branch = resolvedSearchParams?.branch;
  const lang = resolvedSearchParams?.lang;
  const slug = slugParts.join("/");
  const canonicalPath = docCanonicalPath(decodedOwner, decodedRepo, slug, branch, lang);
  const fallbackTitle = `${slugParts.at(-1) ?? "Documentation"} - ${repoTitle(decodedOwner, decodedRepo)}`;
  const data = await getDocData(decodedOwner, decodedRepo, slug, branch, lang);

  if (!data) {
    return noIndexMetadata(
      fallbackTitle,
      `Documentation was not found in ${repoTitle(decodedOwner, decodedRepo)}.`,
      canonicalPath
    );
  }

  const docTitle = extractMarkdownTitle(data.doc.content, slugParts.at(-1) ?? slug);
  const title = `${docTitle} - ${repoTitle(decodedOwner, decodedRepo)}`;
  const description = createMarkdownDescription(
    data.doc.content,
    `Documentation for ${repoTitle(decodedOwner, decodedRepo)}.`
  );

  return indexableMetadata({
    title,
    description,
    canonicalPath,
    type: "article",
  });
}

export default async function RepoDocPage({ params, searchParams }: RepoDocPageProps) {
  const { owner, repo, slug: slugParts } = await params;
  const decodedOwner = decodeRouteSegment(owner);
  const decodedRepo = decodeRouteSegment(repo);
  const resolvedSearchParams = await searchParams;
  const branch = resolvedSearchParams?.branch;
  const lang = resolvedSearchParams?.lang;
  const slug = slugParts.join("/");

  const data = await getDocData(decodedOwner, decodedRepo, slug, branch, lang);
  
  // 文档不存在，但保留侧边栏（由layout提供）
  if (!data) {
    const redirectSlug = await getDirectoryRedirectSlug(decodedOwner, decodedRepo, slug, branch, lang);
    if (redirectSlug) {
      redirect(`${buildRepoDocPath(decodedOwner, decodedRepo, redirectSlug)}${buildQueryString(branch, lang)}`);
    }

    notFound();
  }

  const { doc, headings } = data;
  const locale = await getLocale();
  const t = await getTranslations("common");
  const docTitle = extractMarkdownTitle(doc.content, slugParts.at(-1) ?? slug);
  const description = createMarkdownDescription(doc.content, `Documentation for ${repoTitle(decodedOwner, decodedRepo)}.`);
  const canonicalPath = docCanonicalPath(decodedOwner, decodedRepo, slug, branch, lang);
  const jsonLd = createTechArticleJsonLd({
    title: docTitle,
    description,
    canonicalPath,
    owner: decodedOwner,
    repo: decodedRepo,
    language: lang || locale,
  });

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6 xl:h-full xl:min-h-0 xl:flex-row">
      <article className="min-w-0 flex-1 xl:wiki-scrollbar xl:min-h-0 xl:overflow-y-auto xl:pr-2">
        <script
          type="application/ld+json"
          dangerouslySetInnerHTML={{ __html: safeJsonLd(jsonLd) }}
        />
        <MarkdownRenderer content={doc.content} language={locale} />
        <SourceFiles
          files={doc.sourceFiles || []}
          gitUrl={doc.gitUrl ?? undefined}
          branch={doc.branch ?? branch}
        />
      </article>
      {headings.length > 0 && (
        <aside className="xl:flex xl:min-h-0 xl:w-64 xl:shrink-0">
          <div className="wiki-scrollbar rounded-xl border border-border/70 bg-muted/20 p-3 xl:h-full xl:min-h-0 xl:overflow-y-auto">
            <div className="mb-2 text-sm font-semibold">{t("repository.tableOfContents")}</div>
            <nav className="space-y-1.5">
              {headings.map((heading) => (
                <a
                  key={heading.id}
                  href={`#${heading.id}`}
                  className="block text-[13px] leading-5 text-muted-foreground hover:text-foreground"
                  style={{ paddingLeft: `${(heading.level - 1) * 12}px` }}
                >
                  {heading.text}
                </a>
              ))}
            </nav>
          </div>
        </aside>
      )}
    </div>
  );
}
