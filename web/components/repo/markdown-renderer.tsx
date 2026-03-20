"use client";

import React, { useMemo, useState, useEffect, useId, useCallback } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { Prism as SyntaxHighlighter } from "react-syntax-highlighter";
import { oneDark, oneLight } from "react-syntax-highlighter/dist/esm/styles/prism";
import { useTheme } from "next-themes";
import { slugifyHeading } from "@/lib/markdown";
import { Check, Copy, X, Maximize2, ZoomIn, ZoomOut, RotateCcw } from "lucide-react";

interface MarkdownRendererProps {
  content: string;
  language?: string;
}

interface CodeBlockProps {
  code: string;
  language: string;
  isDark: boolean;
  locale: "zh" | "en";
}

const repoMarkdownText = {
  zh: {
    zoomOut: "缩小",
    zoomIn: "放大",
    resetView: "重置视图",
    close: "关闭 (ESC)",
    hint: "滚轮缩放 · 拖动平移 · ESC 关闭",
    loading: "加载图表中...",
    enlarge: "点击放大查看",
    copyCode: "复制代码",
  },
  en: {
    zoomOut: "Zoom out",
    zoomIn: "Zoom in",
    resetView: "Reset view",
    close: "Close (ESC)",
    hint: "Scroll to zoom · Drag to pan · ESC to close",
    loading: "Loading diagram...",
    enlarge: "Click to enlarge",
    copyCode: "Copy code",
  },
} as const;

// 清理 mermaid 在 body 中创建的错误元素
function cleanupMermaidErrors(diagramId: string) {
  // 清理指定 id 的元素
  const element = document.getElementById(diagramId);
  if (element) {
    element.remove();
  }
  
  // 清理 mermaid 可能创建的 d + id 格式的元素
  const dElement = document.getElementById(`d${diagramId}`);
  if (dElement) {
    dElement.remove();
  }
  
  // 清理 body 直接子元素中的 mermaid 错误元素
  document.querySelectorAll('body > div[id^="mermaid-"], body > div[id^="d"], body > svg[id^="mermaid-"]').forEach((el) => {
    // 检查是否是 mermaid 错误元素（通常包含错误文本或是空的 svg）
    if (el.textContent?.includes('Syntax error') || el.innerHTML === '') {
      el.remove();
    }
  });
}

// Mermaid 全屏模态框组件 - 支持缩放和拖动
function MermaidFullscreenModal({ 
  svg, 
  isOpen, 
  onClose,
  locale,
}: { 
  svg: string; 
  isOpen: boolean; 
  onClose: () => void;
  locale: "zh" | "en";
}) {
  const [scale, setScale] = useState(1);
  const [position, setPosition] = useState({ x: 0, y: 0 });
  const [isDragging, setIsDragging] = useState(false);
  const [dragStart, setDragStart] = useState({ x: 0, y: 0 });
  const containerRef = React.useRef<HTMLDivElement>(null);
  const copy = repoMarkdownText[locale];

  // 重置状态
  const resetView = useCallback(() => {
    setScale(1);
    setPosition({ x: 0, y: 0 });
  }, []);

  // 缩放
  const handleZoomIn = useCallback(() => {
    setScale((s) => Math.min(s + 0.25, 4));
  }, []);

  const handleZoomOut = useCallback(() => {
    setScale((s) => Math.max(s - 0.25, 0.25));
  }, []);

  // 鼠标滚轮缩放
  const handleWheel = useCallback((e: React.WheelEvent) => {
    e.preventDefault();
    const delta = e.deltaY > 0 ? -0.1 : 0.1;
    setScale((s) => Math.min(Math.max(s + delta, 0.25), 4));
  }, []);

  // 拖动开始
  const handleMouseDown = useCallback((e: React.MouseEvent) => {
    if (e.button !== 0) return; // 只响应左键
    setIsDragging(true);
    setDragStart({ x: e.clientX - position.x, y: e.clientY - position.y });
  }, [position]);

  // 拖动中
  const handleMouseMove = useCallback((e: React.MouseEvent) => {
    if (!isDragging) return;
    setPosition({
      x: e.clientX - dragStart.x,
      y: e.clientY - dragStart.y,
    });
  }, [isDragging, dragStart]);

  // 拖动结束
  const handleMouseUp = useCallback(() => {
    setIsDragging(false);
  }, []);

  // ESC 键关闭，重置状态
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        onClose();
      }
    };

    if (isOpen) {
      document.addEventListener("keydown", handleKeyDown);
      document.body.style.overflow = "hidden";
    } else {
      // 关闭时重置状态
      resetView();
    }

    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      document.body.style.overflow = "";
    };
  }, [isOpen, onClose, resetView]);

  if (!isOpen) return null;

  return (
    <div 
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 backdrop-blur-sm"
      onClick={onClose}
    >
      {/* 工具栏 */}
      <div 
        className="absolute left-1/2 top-4 z-50 flex -translate-x-1/2 items-center gap-2 rounded-full bg-background/90 px-4 py-2 shadow-lg"
        onClick={(e) => e.stopPropagation()}
      >
        <button
          onClick={handleZoomOut}
          className="rounded-md p-1.5 transition-colors hover:bg-muted"
          title={copy.zoomOut}
        >
          <ZoomOut className="h-5 w-5" />
        </button>
        <span className="min-w-[4rem] text-center text-sm font-medium">
          {Math.round(scale * 100)}%
        </span>
        <button
          onClick={handleZoomIn}
          className="rounded-md p-1.5 transition-colors hover:bg-muted"
          title={copy.zoomIn}
        >
          <ZoomIn className="h-5 w-5" />
        </button>
        <div className="mx-2 h-5 w-px bg-border" />
        <button
          onClick={resetView}
          className="rounded-md p-1.5 transition-colors hover:bg-muted"
          title={copy.resetView}
        >
          <RotateCcw className="h-5 w-5" />
        </button>
      </div>

      {/* 关闭按钮 */}
      <button
        onClick={onClose}
        className="absolute right-4 top-4 z-50 rounded-full bg-background/90 p-2 text-foreground shadow-lg transition-colors hover:bg-muted"
        title={copy.close}
      >
        <X className="h-6 w-6" />
      </button>

      {/* 图表容器 */}
      <div 
        ref={containerRef}
        className="h-full w-full overflow-hidden"
        onClick={(e) => e.stopPropagation()}
        onWheel={handleWheel}
        onMouseDown={handleMouseDown}
        onMouseMove={handleMouseMove}
        onMouseUp={handleMouseUp}
        onMouseLeave={handleMouseUp}
        style={{ cursor: isDragging ? "grabbing" : "grab" }}
      >
        <div 
          className="flex h-full w-full items-center justify-center"
          style={{
            transform: `translate(${position.x}px, ${position.y}px) scale(${scale})`,
            transition: isDragging ? "none" : "transform 0.1s ease-out",
          }}
        >
          <div 
            className="rounded-lg bg-background p-6 shadow-2xl"
            dangerouslySetInnerHTML={{ __html: svg }}
          />
        </div>
      </div>

      {/* 操作提示 */}
      <div className="absolute bottom-4 left-1/2 -translate-x-1/2 rounded-full bg-background/70 px-4 py-2 text-xs text-muted-foreground">
        {copy.hint}
      </div>
    </div>
  );
}

// Mermaid 图表组件
function MermaidDiagram({ code, isDark, locale }: { code: string; isDark: boolean; locale: "zh" | "en" }) {
  const id = useId().replace(/:/g, "");
  const [svg, setSvg] = useState<string>("");
  const [error, setError] = useState<string>("");
  const [isFullscreen, setIsFullscreen] = useState(false);
  const copy = repoMarkdownText[locale];

  const handleOpenFullscreen = useCallback(() => {
    setIsFullscreen(true);
  }, []);

  const handleCloseFullscreen = useCallback(() => {
    setIsFullscreen(false);
  }, []);

  useEffect(() => {
    const renderDiagram = async () => {
      const diagramId = `mermaid-${id}`;
      try {
        const mermaid = (await import("mermaid")).default;
        mermaid.initialize({
          startOnLoad: false,
          theme: isDark ? "dark" : "default",
          securityLevel: "loose",
          suppressErrorRendering: true,
        });

        const { svg } = await mermaid.render(diagramId, code);
        setSvg(svg);
        setError("");
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to render diagram");
        setSvg("");
      } finally {
        // 无论成功失败，都清理可能残留的错误元素
        cleanupMermaidErrors(diagramId);
      }
    };

    renderDiagram();
  }, [code, isDark, id]);

  if (error) {
    // 渲染失败时显示为普通代码块
    return <CodeBlock code={code} language="mermaid" isDark={isDark} locale={locale} />;
  }

  if (!svg) {
    return (
      <div className="my-4 flex items-center justify-center rounded-lg border border-border bg-muted/50 p-8 not-prose">
        <div className="text-sm text-muted-foreground">{copy.loading}</div>
      </div>
    );
  }

  return (
    <>
      <div 
        className="group relative my-4 flex cursor-pointer justify-center overflow-auto rounded-lg border border-border bg-background p-4 not-prose transition-shadow hover:shadow-md"
        onClick={handleOpenFullscreen}
        title={copy.enlarge}
      >
        <div 
          className="pointer-events-none"
          dangerouslySetInnerHTML={{ __html: svg }}
        />
        <div className="absolute right-2 top-2 rounded-md bg-background/80 p-1.5 opacity-0 transition-opacity group-hover:opacity-100">
          <Maximize2 className="h-4 w-4 text-muted-foreground" />
        </div>
      </div>
        <MermaidFullscreenModal 
          svg={svg} 
          isOpen={isFullscreen} 
          onClose={handleCloseFullscreen}
          locale={locale}
        />
    </>
  );
}

function CodeBlock({ code, language, isDark, locale }: CodeBlockProps) {
  const [copied, setCopied] = useState(false);
  const copy = repoMarkdownText[locale];

  const handleCopy = async () => {
    await navigator.clipboard.writeText(code);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="group relative my-4 not-prose">
      <button
        onClick={handleCopy}
        className="absolute right-2 top-2 z-10 rounded-md bg-background/80 p-2 opacity-0 transition-opacity hover:bg-muted group-hover:opacity-100"
        title={copy.copyCode}
      >
        {copied ? (
          <Check className="h-4 w-4 text-green-500" />
        ) : (
          <Copy className="h-4 w-4 text-muted-foreground" />
        )}
      </button>
      {language && (
        <span className="absolute left-3 top-2 z-10 text-xs text-muted-foreground">
          {language}
        </span>
      )}
      <SyntaxHighlighter
        style={isDark ? oneDark : oneLight}
        language={language || "text"}
        PreTag="div"
        className="!rounded-lg !border !border-border"
        showLineNumbers={code.split("\n").length > 3}
        customStyle={{
          margin: 0,
          borderRadius: "0.5rem",
          fontSize: "0.875rem",
          padding: "1rem",
          paddingTop: language ? "2rem" : "1rem",
        }}
      >
        {code}
      </SyntaxHighlighter>
    </div>
  );
}

function getText(children: React.ReactNode): string {
  if (typeof children === "string") {
    return children;
  }

  if (Array.isArray(children)) {
    return children.map((child) => getText(child)).join("");
  }

  if (React.isValidElement(children)) {
    const props = children.props as { children?: React.ReactNode };
    return getText(props.children);
  }

  return "";
}

// 预先从 markdown 中提取所有 heading 文本，用于生成唯一 id
function extractHeadingTexts(markdown: string): string[] {
  const lines = markdown.split(/\r?\n/);
  const texts: string[] = [];
  let inCodeBlock = false;

  for (const line of lines) {
    const trimmed = line.trim();
    if (trimmed.startsWith("```")) {
      inCodeBlock = !inCodeBlock;
      continue;
    }
    if (inCodeBlock) continue;

    const match = /^(#{1,6})\s+(.+)$/.exec(trimmed);
    if (match) {
      const text = match[2].replace(/#+$/, "").trim()
        .replace(/`/g, "")
        .replace(/\[(.*?)\]\([^)]*\)/g, "$1")
        .replace(/[*_~]/g, "")
        .replace(/\s+/g, " ")
        .trim();
      texts.push(text);
    }
  }
  return texts;
}

// 生成 heading id 映射
function createHeadingIdMap(texts: string[]): Map<string, string[]> {
  const idMap = new Map<string, string[]>();
  const counts = new Map<string, number>();

  for (const text of texts) {
    const base = slugifyHeading(text) || "section";
    const current = counts.get(base) ?? 0;
    counts.set(base, current + 1);
    const id = current === 0 ? base : `${base}-${current}`;
    
    if (!idMap.has(text)) {
      idMap.set(text, []);
    }
    idMap.get(text)!.push(id);
  }

  return idMap;
}

export function MarkdownRenderer({ content, language }: MarkdownRendererProps) {
  const locale = language === "en" ? "en" : "zh";
  const { resolvedTheme } = useTheme();
  const [mounted, setMounted] = useState(false);
  
  // 等待客户端挂载后再确定主题
  useEffect(() => {
    setMounted(true);
  }, []);
  
  // 默认使用暗色主题，挂载后使用实际主题
  const isDark = mounted ? resolvedTheme === "dark" : true;
  
  // 预计算所有 heading 的 id
  const headingIdMap = useMemo(() => {
    const texts = extractHeadingTexts(content);
    return createHeadingIdMap(texts);
  }, [content]);

  // 追踪每个 heading text 使用的次数
  const usedCounts = useMemo(() => new Map<string, number>(), [content]);

  const getHeadingId = (text: string) => {
    const normalizedText = text
      .replace(/`/g, "")
      .replace(/\[(.*?)\]\([^)]*\)/g, "$1")
      .replace(/[*_~]/g, "")
      .replace(/\s+/g, " ")
      .trim();
    
    const ids = headingIdMap.get(normalizedText);
    if (!ids || ids.length === 0) {
      return slugifyHeading(normalizedText) || "section";
    }
    
    const usedCount = usedCounts.get(normalizedText) ?? 0;
    usedCounts.set(normalizedText, usedCount + 1);
    return ids[usedCount] ?? ids[0];
  };

  return (
    <article className="prose prose-neutral dark:prose-invert max-w-none prose-pre:p-0 prose-pre:bg-transparent prose-code:before:content-none prose-code:after:content-none">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          h1: ({ children, ...props }) => {
            const id = getHeadingId(getText(children));
            return (
              <h1 id={id} data-toc="" className="scroll-mt-24" {...props}>
                {children}
              </h1>
            );
          },
          h2: ({ children, ...props }) => {
            const id = getHeadingId(getText(children));
            return (
              <h2 id={id} data-toc="" className="scroll-mt-24" {...props}>
                {children}
              </h2>
            );
          },
          h3: ({ children, ...props }) => {
            const id = getHeadingId(getText(children));
            return (
              <h3 id={id} data-toc="" className="scroll-mt-24" {...props}>
                {children}
              </h3>
            );
          },
          h4: ({ children, ...props }) => {
            const id = getHeadingId(getText(children));
            return (
              <h4 id={id} data-toc="" className="scroll-mt-24" {...props}>
                {children}
              </h4>
            );
          },
          h5: ({ children, ...props }) => {
            const id = getHeadingId(getText(children));
            return (
              <h5 id={id} data-toc="" className="scroll-mt-24" {...props}>
                {children}
              </h5>
            );
          },
          h6: ({ children, ...props }) => {
            const id = getHeadingId(getText(children));
            return (
              <h6 id={id} data-toc="" className="scroll-mt-24" {...props}>
                {children}
              </h6>
            );
          },
          code: ({ className, children, ...props }) => {
            const match = /language-(\w+)/.exec(className || "");
            const language = match ? match[1] : "";
            const codeString = String(children).replace(/\n$/, "");
            
            // 处理 mermaid 图表
            if (language === "mermaid") {
              return <MermaidDiagram code={codeString} isDark={isDark} locale={locale} />;
            }
            
            // Check if this is a code block (has language) or inline code
            const isCodeBlock = match || codeString.includes("\n");
            
            if (isCodeBlock) {
              return <CodeBlock code={codeString} language={language} isDark={isDark} locale={locale} />;
            }
            
            return (
              <code className="rounded bg-muted px-1.5 py-0.5 text-sm font-mono" {...props}>
                {children}
              </code>
            );
          },
          pre: ({ children }) => {
            return <>{children}</>;
          },
          table: ({ children, ...props }) => (
            <div className="my-4 overflow-x-auto">
              <table className="w-full border-collapse" {...props}>
                {children}
              </table>
            </div>
          ),
          th: ({ children, ...props }) => (
            <th className="border border-border bg-muted px-4 py-2 text-left font-semibold" {...props}>
              {children}
            </th>
          ),
          td: ({ children, ...props }) => (
            <td className="border border-border px-4 py-2" {...props}>
              {children}
            </td>
          ),
        }}
      >
        {content}
      </ReactMarkdown>
    </article>
  );
}
