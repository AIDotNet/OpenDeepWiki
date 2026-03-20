"use client";

const messages = {
  en: {
    title: "Page failed to load",
    description: "An unexpected error occurred. Please try again in a moment.",
    back: "Back to Home",
  },
  zh: {
    title: "页面加载失败",
    description: "发生了未预期的错误，请稍后重试。",
    back: "返回首页",
  },
} as const;

export default function GlobalError() {
  const locale = typeof document !== "undefined" && document.cookie.includes("NEXT_LOCALE=en") ? "en" : "zh";
  const copy = messages[locale];

  return (
    <main className="flex min-h-screen flex-col items-center justify-center px-6 text-center">
      <h1 className="text-3xl font-semibold">{copy.title}</h1>
      <p className="mt-3 text-sm text-muted-foreground">
        {copy.description}
      </p>
      <a
        href="/"
        className="mt-6 inline-flex h-10 items-center justify-center rounded-md bg-primary px-4 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
      >
        {copy.back}
      </a>
    </main>
  );
}
