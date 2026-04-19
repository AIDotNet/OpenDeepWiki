"use client";

import { useEffect, useRef, useState } from "react";
import mermaid from "mermaid";
import { useTheme } from "next-themes";
import { CodeBlock, Pre } from "fumadocs-ui/components/codeblock";

export function Mermaid({ chart }: { chart: string }) {
  const { resolvedTheme } = useTheme();
  const containerRef = useRef<HTMLDivElement>(null);
  const [svg, setSvg] = useState<string>("");
  const [error, setError] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string>("");

  useEffect(() => {
    if (!chart || !resolvedTheme) return;

    let isMounted = true;

    const renderChart = async () => {
      try {
        mermaid.initialize({
          startOnLoad: false,
          theme: resolvedTheme === "dark" ? "dark" : "default",
          flowchart: {
            useMaxWidth: true,
            htmlLabels: true,
          },
          sequence: {
            useMaxWidth: true,
          },
        });

        const parseResult = await mermaid.parse(chart, { suppressErrors: true });

        if (parseResult === false) {
          if (isMounted) {
            setError(true);
            setErrorMessage("Mermaid 语法错误，无法渲染图表");
          }
          return;
        }

        const id = `mermaid-${Math.random().toString(36).slice(2)}`;
        const { svg: renderedSvg } = await mermaid.render(id, chart);
        if (isMounted) {
          setSvg(renderedSvg);
          setError(false);
          setErrorMessage("");
        }
      } catch (err) {
        if (isMounted) {
          console.error("Mermaid render error:", err);
          setError(true);
          setErrorMessage(err instanceof Error ? err.message : "渲染失败");
        }
      }
    };

    renderChart();

    return () => {
      isMounted = false;
    };
  }, [chart, resolvedTheme]);

  if (error) {
    return (
      <CodeBlock title="Mermaid (语法错误)">
        <Pre>{chart}</Pre>
      </CodeBlock>
    );
  }

  if (!svg) {
    return (
      <div className="flex items-center justify-center p-4 min-h-[100px]">
        <div className="animate-pulse text-fd-muted-foreground">Loading diagram...</div>
      </div>
    );
  }

  return (
    <div
      ref={containerRef}
      className="mermaid-container overflow-x-auto py-4 [&_svg]:max-w-full"
      dangerouslySetInnerHTML={{ __html: svg }}
    />
  );
}