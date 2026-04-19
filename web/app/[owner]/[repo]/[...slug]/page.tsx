import { fetchRepoDoc } from "@/lib/repository-api";
import { DocNotFound } from "@/components/repo/doc-not-found";
import { SourceFiles } from "@/components/repo/source-files";
import { decodeRouteSegment } from "@/lib/repo-route";
import { DocsPage, DocsBody } from "fumadocs-ui/layouts/docs/page";
import { getMDXComponents } from "@/components/mdx-components";
import { mdxCompiler } from "@/lib/mdx-compiler";

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

async function getDocData(owner: string, repo: string, slug: string, branch?: string, lang?: string) {
  try {
    const doc = await fetchRepoDoc(owner, repo, slug, branch, lang);
    if (!doc.exists) {
      return null;
    }
    return { doc };
  } catch {
    return null;
  }
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
  
  if (!data) {
    return (
      <DocsPage>
        <DocsBody>
          <DocNotFound slug={slug} />
        </DocsBody>
      </DocsPage>
    );
  }

  const { doc } = data;

  const compiled = await mdxCompiler.compile({
    source: doc.content,
  });

  const MdxContent = compiled.body;
  const toc = compiled.toc;

  return (
    <DocsPage toc={toc}>
      <DocsBody>
        <MdxContent components={getMDXComponents()} />
        <SourceFiles 
          files={doc.sourceFiles || []} 
          branch={branch}
        />
      </DocsBody>
    </DocsPage>
  );
}