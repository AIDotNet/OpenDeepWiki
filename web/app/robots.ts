import type { MetadataRoute } from "next";
import { absoluteUrl } from "@/lib/repo-seo";

// ISR：运行时渲染一次后缓存复用（保留对 SITE_URL 等运行时环境变量的读取）
export const revalidate = 3600;

export default function robots(): MetadataRoute.Robots {
  return {
    rules: [
      {
        userAgent: "*",
        allow: "/",
        disallow: [
          "/admin/",
          "/api/",
          "/auth",
          "/oauth/",
          "/private",
          "/settings",
          "/share/",
          "/*?*branch=",
          "/*?*lang=",
        ],
      },
    ],
    sitemap: absoluteUrl("/sitemap.xml"),
  };
}
