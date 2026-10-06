import type { BaseLayoutProps } from 'fumadocs-ui/layouts/shared';
import { GitBranch } from 'lucide-react';

export function baseOptions(): BaseLayoutProps {
  return {
    nav: {
      title: 'OpenDeepWiki',
    },
    links: [
      {
        text: '文档',
        url: '/docs',
      },
      {
        type: 'icon',
        text: 'GitHub',
        url: 'https://github.com/AIDotNet/OpenDeepWiki',
        icon: <GitBranch />,
        external: true,
      },
    ],
  };
}
