import type { ReactNode } from "react";
import RouteProviders from "@/app/route-providers";

export const dynamic = "force-dynamic";

export default function OwnerLayout({ children }: { children: ReactNode }) {
  return <RouteProviders>{children}</RouteProviders>;
}
