import Link from 'next/link';
import {
  Zap,
  Globe,
  GitBranch,
  Puzzle,
  Brain,
  Plug,
  ArrowRight,
  Terminal,
  Settings,
  Rocket,
} from 'lucide-react';

const features = [
  {
    icon: Zap,
    title: '快速转换',
    description:
      '使用 AI 驱动的分析，在几分钟内将任何 GitHub、GitLab 或 Gitee 仓库转换为全面的 wiki。',
  },
  {
    icon: Globe,
    title: '多语言支持',
    description:
      '使用内置翻译支持为全球团队生成多种语言的文档。',
  },
  {
    icon: GitBranch,
    title: '代码结构图',
    description:
      '使用 Mermaid 自动生成架构图和代码结构可视化。',
  },
  {
    icon: Puzzle,
    title: '自定义模型支持',
    description:
      '使用 OpenAI、Azure OpenAI、Anthropic 或任何兼容的 API 提供商进行 wiki 生成。',
  },
  {
    icon: Brain,
    title: 'AI 智能分析',
    description:
      '利用 Semantic Kernel 进行智能代码分析、目录生成和内容创建。',
  },
  {
    icon: Plug,
    title: 'MCP 协议',
    description:
      '通过模型上下文协议（MCP）使用 Streamable HTTP 或 SSE 模式与 AI 工具集成。',
  },
];

const steps = [
  {
    number: 1,
    icon: Terminal,
    title: '克隆和配置',
    description: '克隆仓库并在环境变量中设置你的 AI 提供商 API 密钥。',
    code: 'git clone https://github.com/AIDotNet/OpenDeepWiki.git',
  },
  {
    number: 2,
    icon: Settings,
    title: '使用 Docker 启动',
    description: '使用 Docker Compose 一条命令启动后端 API 和前端。',
    code: 'docker compose up -d',
  },
  {
    number: 3,
    icon: Rocket,
    title: '生成 Wiki',
    description: '提交仓库 URL，让 AI 分析代码库以生成全面的文档。',
    code: 'Open http://localhost:3000',
  },
];

export default function HomePage() {
  return (
    <>
      {/* Hero Section */}
      <section className="flex flex-col items-center justify-center text-center px-6 py-24 md:py-32">
        <div className="inline-flex items-center gap-2 rounded-full border px-4 py-1.5 text-sm text-fd-muted-foreground mb-6">
          <Zap className="size-4" />
          <span>AI 驱动的代码知识库</span>
        </div>
        <h1 className="text-4xl font-extrabold tracking-tight sm:text-5xl md:text-6xl">
          OpenDeepWiki
        </h1>
        <p className="mt-4 max-w-2xl text-lg text-fd-muted-foreground">
          使用 AI 驱动的分析将任何 GitHub、GitLab 或 Gitee 仓库转换为全面的 wiki。
          更快地理解代码库，更快地让开发者上手，并保持文档始终最新。
        </p>
        <div className="mt-8 flex flex-wrap items-center justify-center gap-4">
          <Link
            href="/docs"
            className="inline-flex items-center gap-2 rounded-lg bg-fd-primary px-6 py-3 text-sm font-medium text-fd-primary-foreground shadow transition-colors hover:bg-fd-primary/90"
          >
            快速开始
            <ArrowRight className="size-4" />
          </Link>
          <a
            href="https://github.com/AIDotNet/OpenDeepWiki"
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center gap-2 rounded-lg border px-6 py-3 text-sm font-medium transition-colors hover:bg-fd-accent"
          >
            <GitBranch className="size-4" />
            GitHub
          </a>
        </div>
      </section>

      {/* Features Grid */}
      <section className="px-6 py-16 md:py-24">
        <div className="mx-auto max-w-6xl">
          <h2 className="text-center text-3xl font-bold tracking-tight">
            核心功能
          </h2>
          <p className="mt-3 text-center text-fd-muted-foreground">
            将代码仓库转换为活跃文档所需的一切。
          </p>
          <div className="mt-12 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
            {features.map((feature) => (
              <div
                key={feature.title}
                className="group rounded-xl border bg-fd-card p-6 transition-colors hover:bg-fd-accent/50"
              >
                <div className="mb-4 inline-flex rounded-lg border p-2.5">
                  <feature.icon className="size-5 text-fd-primary" />
                </div>
                <h3 className="text-lg font-semibold">{feature.title}</h3>
                <p className="mt-2 text-sm leading-relaxed text-fd-muted-foreground">
                  {feature.description}
                </p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* Quick Start Section */}
      <section className="border-t px-6 py-16 md:py-24">
        <div className="mx-auto max-w-4xl">
          <h2 className="text-center text-3xl font-bold tracking-tight">
            快速开始
          </h2>
          <p className="mt-3 text-center text-fd-muted-foreground">
            三个简单步骤即可启动并运行。
          </p>
          <div className="mt-12 space-y-8">
            {steps.map((step) => (
              <div key={step.number} className="flex gap-5">
                <div className="flex flex-col items-center">
                  <div className="flex size-10 shrink-0 items-center justify-center rounded-full bg-fd-primary text-sm font-bold text-fd-primary-foreground">
                    {step.number}
                  </div>
                  {step.number < steps.length && (
                    <div className="mt-2 h-full w-px bg-fd-border" />
                  )}
                </div>
                <div className="pb-8">
                  <div className="flex items-center gap-2">
                    <step.icon className="size-4 text-fd-muted-foreground" />
                    <h3 className="font-semibold">{step.title}</h3>
                  </div>
                  <p className="mt-1 text-sm text-fd-muted-foreground">
                    {step.description}
                  </p>
                  <pre className="mt-3 overflow-x-auto rounded-lg border bg-fd-secondary/50 px-4 py-3 text-sm">
                    <code>{step.code}</code>
                  </pre>
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* Footer CTA */}
      <section className="border-t px-6 py-16 md:py-24">
        <div className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight">
            准备好开始了吗？
          </h2>
          <p className="mt-3 text-fd-muted-foreground">
            深入了解文档或在 GitHub 上探索源代码。
          </p>
          <div className="mt-8 flex flex-wrap items-center justify-center gap-4">
            <Link
              href="/docs"
              className="inline-flex items-center gap-2 rounded-lg bg-fd-primary px-6 py-3 text-sm font-medium text-fd-primary-foreground shadow transition-colors hover:bg-fd-primary/90"
            >
              阅读文档
              <ArrowRight className="size-4" />
            </Link>
            <a
              href="https://github.com/AIDotNet/OpenDeepWiki"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2 rounded-lg border px-6 py-3 text-sm font-medium transition-colors hover:bg-fd-accent"
            >
              在 GitHub 上查看
            </a>
          </div>
        </div>
      </section>
    </>
  );
}
