import { createCompiler } from '@fumadocs/mdx-remote';
import { remarkHeading, remarkMdxMermaid } from 'fumadocs-core/mdx-plugins';

export const mdxCompiler = createCompiler({
  remarkPlugins: [remarkHeading, remarkMdxMermaid],
  rehypePlugins: [],
});