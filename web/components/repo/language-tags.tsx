"use client";

import { useEffect, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { getAvailableLanguages, type LanguageInfo } from "@/lib/recommendation-api";
import { cn } from "@/lib/utils";
import { Code2 } from "lucide-react";

interface LanguageTagsProps {
  selectedLanguage: string | null;
  onLanguageChange: (language: string | null) => void;
  className?: string;
  languages?: LanguageInfo[];
}

// 语言颜色映射
const languageColors: Record<string, string> = {
  TypeScript: "bg-blue-500/10 text-blue-500 hover:bg-blue-500/20 border-blue-500/20",
  JavaScript: "bg-yellow-500/10 text-yellow-500 hover:bg-yellow-500/20 border-yellow-500/20",
  Python: "bg-green-500/10 text-green-500 hover:bg-green-500/20 border-green-500/20",
  Java: "bg-orange-500/10 text-orange-500 hover:bg-orange-500/20 border-orange-500/20",
  "C#": "bg-purple-500/10 text-purple-500 hover:bg-purple-500/20 border-purple-500/20",
  Go: "bg-cyan-500/10 text-cyan-500 hover:bg-cyan-500/20 border-cyan-500/20",
  Rust: "bg-red-500/10 text-red-500 hover:bg-red-500/20 border-red-500/20",
  C: "bg-gray-500/10 text-gray-400 hover:bg-gray-500/20 border-gray-500/20",
  "C++": "bg-pink-500/10 text-pink-500 hover:bg-pink-500/20 border-pink-500/20",
  PHP: "bg-indigo-500/10 text-indigo-500 hover:bg-indigo-500/20 border-indigo-500/20",
  Ruby: "bg-red-600/10 text-red-400 hover:bg-red-600/20 border-red-600/20",
  Swift: "bg-orange-600/10 text-orange-400 hover:bg-orange-600/20 border-orange-600/20",
  Kotlin: "bg-violet-500/10 text-violet-500 hover:bg-violet-500/20 border-violet-500/20",
  Scala: "bg-red-500/10 text-red-400 hover:bg-red-500/20 border-red-500/20",
  Shell: "bg-emerald-500/10 text-emerald-500 hover:bg-emerald-500/20 border-emerald-500/20",
  Vue: "bg-green-600/10 text-green-400 hover:bg-green-600/20 border-green-600/20",
};

const defaultColor = "bg-muted hover:bg-muted/80 text-muted-foreground border-border";

export function LanguageTags({ selectedLanguage, onLanguageChange, className, languages: languagesProp }: LanguageTagsProps) {
  const [languageData, setLanguageData] = useState<LanguageInfo[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    if (languagesProp) {
      setLanguageData(languagesProp);
      setIsLoading(false);
      return;
    }
    const loadLanguages = async () => {
      try {
        const response = await getAvailableLanguages();
        setLanguageData(response.languages);
      } catch (error) {
        console.error("Failed to load languages:", error);
      } finally {
        setIsLoading(false);
      }
    };
    loadLanguages();
  }, [languagesProp]);

  if (isLoading) {
    return (
      <div className={cn("flex flex-wrap gap-2", className)}>
        {[1, 2, 3, 4, 5, 6].map((i) => (
          <Skeleton key={i} className="h-7 w-20 rounded-full" />
        ))}
      </div>
    );
  }

  if (languageData.length === 0) {
    return null;
  }

  return (
    <div className={cn("flex flex-wrap items-center gap-2", className)}>
      <Code2 className="h-4 w-4 text-muted-foreground" />
      <Badge
        variant="outline"
        className={cn(
          "cursor-pointer transition-all border",
          selectedLanguage === null
            ? "bg-primary/10 text-primary border-primary/30"
            : defaultColor
        )}
        onClick={() => onLanguageChange(null)}
      >
        全部
      </Badge>
      {languageData.map((lang) => (
        <Badge
          key={lang.name}
          variant="outline"
          className={cn(
            "cursor-pointer transition-all border",
            selectedLanguage === lang.name
              ? languageColors[lang.name] || defaultColor
              : "bg-secondary/50 hover:bg-secondary text-secondary-foreground border-transparent"
          )}
          onClick={() => onLanguageChange(lang.name === selectedLanguage ? null : lang.name)}
        >
          {lang.name}
          <span className="ml-1 text-xs opacity-60">({lang.count})</span>
        </Badge>
      ))}
    </div>
  );
}
