"use client";

import React, { useEffect, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/utils";
import {
  LayoutDashboard,
  GitBranch,
  Settings,
  Shield,
  Users,
  Cog,
  Wrench,
  Home,
  Building2,
  MessageCircle,
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { api } from "@/lib/api-client";
import { useTranslations } from "@/hooks/use-translations";

const getNavItems = (t: (key: string) => string) => [
  {
    href: "/admin",
    icon: LayoutDashboard,
    label: t('common.admin.dashboard'),
  },
  {
    href: "/admin/repositories",
    icon: GitBranch,
    label: t('common.admin.repositories'),
  },
  {
    label: t('common.admin.tools'),
    icon: Wrench,
    children: [
      { href: "/admin/tools/mcps", label: t('common.admin.mcps') },
      { href: "/admin/tools/skills", label: t('common.admin.skills') },
      { href: "/admin/tools/models", label: t('common.admin.models') },
    ],
  },
  {
    href: "/admin/roles",
    icon: Shield,
    label: t('common.admin.roles'),
  },
  {
    href: "/admin/departments",
    icon: Building2,
    label: t('admin.departments.title'),
  },
  {
    href: "/admin/users",
    icon: Users,
    label: t('common.admin.users'),
  },
  {
    href: "/admin/chat-assistant",
    icon: MessageCircle,
    label: t('admin.chatAssistant.title'),
  },
  {
    href: "/admin/chat-providers",
    icon: MessageCircle,
    label: t('admin.chatProviders.title'),
  },
  {
    href: "/admin/settings",
    icon: Cog,
    label: t('common.admin.settings'),
  },
];

interface VersionInfo {
  version: string;
  assemblyVersion: string;
  productName: string;
}

interface AdminSidebarProps {
  onNavigate?: () => void;
}

export function AdminSidebar({ onNavigate }: AdminSidebarProps) {
  const pathname = usePathname();
  const t = useTranslations();
  const navItems = getNavItems(t);
  const [expandedItems, setExpandedItems] = React.useState<string[]>([t('common.admin.tools')]);
  const [versionInfo, setVersionInfo] = useState<VersionInfo | null>(null);

  useEffect(() => {
    api.get<{ success: boolean; data: VersionInfo }>("/api/system/version", { skipAuth: true })
      .then((res) => {
        if (res.success) {
          setVersionInfo(res.data);
        }
      })
      .catch(() => {
        // Ignore version fetch failure
      });
  }, []);

  const isPreview = versionInfo?.version?.toLowerCase().includes("preview");

  const toggleExpand = (label: string) => {
    setExpandedItems((prev) =>
      prev.includes(label)
        ? prev.filter((item) => item !== label)
        : [...prev, label]
    );
  };

  return (
    <aside className="w-64 border-r bg-card flex flex-col h-full">
      <div className="p-4 border-b">
        <h1 className="text-lg font-semibold">{t('common.adminPanel')}</h1>
      </div>

      <nav className="flex-1 p-4 space-y-1 overflow-y-auto">
        {navItems.map((item) => {
          if (item.children) {
            const isExpanded = expandedItems.includes(item.label);
            const isChildActive = item.children.some((child) =>
              pathname.startsWith(child.href)
            );

            return (
              <div key={item.label}>
                <button
                  onClick={() => toggleExpand(item.label)}
                  className={cn(
                    "w-full flex items-center gap-3 px-3 py-2 rounded-md text-sm font-medium transition-colors",
                    isChildActive
                      ? "bg-primary/10 text-primary"
                      : "text-muted-foreground hover:bg-muted hover:text-foreground"
                  )}
                >
                  <item.icon className="h-4 w-4" />
                  <span className="flex-1 text-left">{item.label}</span>
                  <Settings
                    className={cn(
                      "h-4 w-4 transition-transform",
                      isExpanded && "rotate-90"
                    )}
                  />
                </button>
                {isExpanded && (
                  <div className="ml-7 mt-1 space-y-1">
                    {item.children.map((child) => (
                      <Link
                        key={child.href}
                        href={child.href}
                        onClick={onNavigate}
                        className={cn(
                          "block px-3 py-2 rounded-md text-sm transition-colors",
                          pathname === child.href
                            ? "bg-primary/10 text-primary font-medium"
                            : "text-muted-foreground hover:bg-muted hover:text-foreground"
                        )}
                      >
                        {child.label}
                      </Link>
                    ))}
                  </div>
                )}
              </div>
            );
          }

          const isActive = item.href === "/admin"
            ? pathname === item.href
            : pathname === item.href || pathname.startsWith(`${item.href}/`);

          return (
            <Link
              key={item.href}
              href={item.href}
              onClick={onNavigate}
              className={cn(
                "flex items-center gap-3 px-3 py-2 rounded-md text-sm font-medium transition-colors",
                isActive
                  ? "bg-primary/10 text-primary"
                  : "text-muted-foreground hover:bg-muted hover:text-foreground"
              )}
            >
              <item.icon className="h-4 w-4" />
              {item.label}
            </Link>
          );
        })}

        <div className="pt-2 mt-2 border-t">
          <Link
            href="/"
            onClick={onNavigate}
            className="flex items-center gap-3 px-3 py-2 rounded-md text-sm font-medium transition-colors text-muted-foreground hover:bg-muted hover:text-foreground"
          >
            <Home className="h-4 w-4" />
            {t('common.backToHome')}
          </Link>
        </div>
      </nav>

      {versionInfo && (
        <div className="p-4 border-t">
          <div className="flex items-center justify-center gap-2 text-xs text-muted-foreground">
            {isPreview ? (
              <Badge className="text-[10px] px-2 py-0.5 bg-amber-500/20 text-amber-600 dark:text-amber-400 border-amber-500/30 hover:bg-amber-500/30">
                v{versionInfo.version.split('+')[0]}
              </Badge>
            ) : (
              <span>v{versionInfo.version.split('+')[0]}</span>
            )}
          </div>
        </div>
      )}
    </aside>
  );
}
