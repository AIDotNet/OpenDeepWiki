import { RootProvider } from 'fumadocs-ui/provider/next';
import './globals.css';
import { Inter } from 'next/font/google';
import type { Metadata } from 'next';

const inter = Inter({
  subsets: ['latin'],
});

export const metadata: Metadata = {
  title: {
    default: 'OpenDeepWiki',
    template: '%s | OpenDeepWiki',
  },
  description:
    'AI-Driven Code Knowledge Base — Transform any GitHub, GitLab, or Gitee repository into a comprehensive wiki with AI-powered analysis.',
};

export default function Layout({ children }: LayoutProps<'/'>) {
  return (
    <html lang="en" className={inter.className} suppressHydrationWarning>
      <body className="flex flex-col min-h-screen">
        <RootProvider>{children}</RootProvider>
      </body>
    </html>
  );
}
