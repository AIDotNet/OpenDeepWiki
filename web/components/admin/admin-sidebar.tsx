"use client";

import React, { useEffect, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  LayoutDashboard,
  GitBranch,
  Github,
  Settings,
  Shield,
  Users,
  Cog,
  Wrench,
  Home,
  Building2,
  MessageCircle,
  ChevronRight,
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { api } from "@/lib/api-client";
import { useTranslations } from "@/hooks/use-translations";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarMenuSub,
  SidebarMenuSubButton,
  SidebarMenuSubItem,
  SidebarRail,
} from "@/components/animate-ui/components/radix/sidebar";

interface NavItem {
  href?: string;
  icon: React.ComponentType<{ className?: string }>;
  label: string;
  children?: { href: string; label: string }[];
}

const getNavItems = (t: (key: string) => string): NavItem[] => [
  {
    href: "/admin",
    icon: LayoutDashboard,
    label: t("common.admin.dashboard"),
  },
  {
    href: "/admin/repositories",
    icon: GitBranch,
    label: t("common.admin.repositories"),
  },
  {
    href: "/admin/github-import",
    icon: Github,
    label: t("admin.githubImport.title"),
  },
  {
    label: t("common.admin.tools"),
    icon: Wrench,
    children: [
      { href: "/admin/tools/mcps", label: t("common.admin.mcps") },
      { href: "/admin/tools/skills", label: t("common.admin.skills") },
      { href: "/admin/tools/models", label: t("common.admin.models") },
      { href: "/admin/mcp-providers", label: t("admin.mcpProviders.title") },
    ],
  },
  {
    href: "/admin/roles",
    icon: Shield,
    label: t("common.admin.roles"),
  },
  {
    href: "/admin/departments",
    icon: Building2,
    label: t("admin.departments.title"),
  },
  {
    href: "/admin/users",
    icon: Users,
    label: t("common.admin.users"),
  },
  {
    href: "/admin/chat-assistant",
    icon: MessageCircle,
    label: t("admin.chatAssistant.title"),
  },
  {
    href: "/admin/chat-providers",
    icon: MessageCircle,
    label: t("admin.chatProviders.title"),
  },
  {
    href: "/admin/settings",
    icon: Cog,
    label: t("common.admin.settings"),
  },
];

interface VersionInfo {
  version: string;
  assemblyVersion: string;
  productName: string;
}

export function AdminSidebar(props: React.ComponentProps<typeof Sidebar>) {
  const pathname = usePathname();
  const t = useTranslations();
  const navItems = getNavItems(t);
  const [expandedItems, setExpandedItems] = React.useState<string[]>([
    t("common.admin.tools"),
  ]);
  const [versionInfo, setVersionInfo] = useState<VersionInfo | null>(null);

  useEffect(() => {
    api
      .get<{ success: boolean; data: VersionInfo }>("/api/system/version", {
        skipAuth: true,
      })
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
  const displayVersion = versionInfo?.version?.split("+")[0] || "";

  const toggleExpand = (label: string) => {
    setExpandedItems((prev) =>
      prev.includes(label)
        ? prev.filter((item) => item !== label)
        : [...prev, label]
    );
  };

  return (
    <Sidebar collapsible="icon" {...props}>
      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupLabel>{t("common.adminPanel")}</SidebarGroupLabel>
          <SidebarGroupContent>
            <SidebarMenu>
              {navItems.map((item) => {
                if (item.children) {
                  const isExpanded = expandedItems.includes(item.label);
                  const isChildActive = item.children.some((child) =>
                    pathname.startsWith(child.href)
                  );

                  return (
                    <SidebarMenuItem key={item.label}>
                      <SidebarMenuButton
                        tooltip={item.label}
                        isActive={isChildActive}
                        onClick={() => toggleExpand(item.label)}
                      >
                        <item.icon />
                        <span>{item.label}</span>
                        <ChevronRight
                          className={`ml-auto transition-transform ${
                            isExpanded ? "rotate-90" : ""
                          }`}
                        />
                      </SidebarMenuButton>
                      {isExpanded && (
                        <SidebarMenuSub>
                          {item.children.map((child) => (
                            <SidebarMenuSubItem key={child.href}>
                              <SidebarMenuSubButton
                                asChild
                                isActive={pathname === child.href}
                              >
                                <Link href={child.href}>{child.label}</Link>
                              </SidebarMenuSubButton>
                            </SidebarMenuSubItem>
                          ))}
                        </SidebarMenuSub>
                      )}
                    </SidebarMenuItem>
                  );
                }

                const isActive = item.href === "/admin"
                  ? pathname === item.href
                  : pathname === item.href ||
                    pathname.startsWith(`${item.href}/`);

                return (
                  <SidebarMenuItem key={item.href}>
                    <SidebarMenuButton
                      asChild
                      tooltip={item.label}
                      isActive={isActive}
                    >
                      <Link href={item.href!}>
                        <item.icon />
                        <span>{item.label}</span>
                      </Link>
                    </SidebarMenuButton>
                  </SidebarMenuItem>
                );
              })}
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>
      </SidebarContent>

      <SidebarFooter>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton asChild tooltip={t("common.backToHome")}>
              <Link href="/">
                <Home />
                <span>{t("common.backToHome")}</span>
              </Link>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
        {displayVersion && (
          <div className="px-3 py-2 border-t">
            <div className="flex items-center justify-center gap-2 text-xs text-muted-foreground">
              {isPreview ? (
                <Badge className="text-[10px] px-2 py-0.5 bg-amber-500/20 text-amber-600 dark:text-amber-400 border-amber-500/30 hover:bg-amber-500/30">
                  v{displayVersion}
                </Badge>
              ) : (
                <span>v{displayVersion}</span>
              )}
            </div>
          </div>
        )}
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
  );
}
