"use client";

import {
    Compass,
    ThumbsUp,
    GitFork,
    Star,
    Bookmark,
    Building2,
    AppWindow,
    Lock,
} from "lucide-react";
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
    SidebarRail,
} from "@/components/animate-ui/components/radix/sidebar";
import React, { useState, useEffect } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTranslations } from "@/hooks/use-translations";
import { useAuth } from "@/contexts/auth-context";
import Image from "next/image";
import { Badge } from "@/components/ui/badge";
import { api } from "@/lib/api-client";

// GitHub icon SVG component
const GithubIcon = ({ className }: { className?: string }) => (
    <svg
        className={className}
        viewBox="0 0 24 24"
        fill="currentColor"
        width="16"
        height="16"
    >
        <path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z" />
    </svg>
);

const itemKeys = [
    { key: "explore", url: "/", icon: Compass, requireAuth: false },
    { key: "recommend", url: "/recommend", icon: ThumbsUp, requireAuth: false },
    { key: "private", url: "/?view=organization", icon: Lock, requireAuth: true },
    { key: "subscribe", url: "/subscribe", icon: Star, requireAuth: true },
    { key: "bookmarks", url: "/bookmarks", icon: Bookmark, requireAuth: true },
    { key: "organizations", url: "/organizations", icon: Building2, requireAuth: false },
    { key: "apps", url: "/apps", icon: AppWindow, requireAuth: true },
];

interface AppSidebarProps extends React.ComponentProps<typeof Sidebar> {
    activeItem?: string;
    onItemClick?: (title: string) => void;
}

interface VersionInfo {
    version: string;
    assemblyVersion: string;
    productName: string;
}

export function AppSidebar({ activeItem, onItemClick, ...props }: AppSidebarProps) {
    const t = useTranslations();
    const router = useRouter();
    const { isAuthenticated } = useAuth();
    const [versionInfo, setVersionInfo] = useState<VersionInfo | null>(null);

    useEffect(() => {
        api.get<{ success: boolean; data: VersionInfo }>("/api/system/version", { skipAuth: true })
            .then((res) => {
                if (res.success) {
                    setVersionInfo(res.data);
                }
            })
            .catch(() => {});
    }, []);

    // Strip commit hash from version (after + sign)
    const displayVersion = versionInfo?.version?.split('+')[0] || '';
    const isPreview = displayVersion.toLowerCase().includes('preview');

    const items = itemKeys.map(item => ({
        title: t(`sidebar.${item.key}`),
        url: item.url,
        icon: item.icon,
        requireAuth: item.requireAuth,
    }));

    const handleItemClick = (item: typeof items[0]) => {
        if (item.requireAuth && !isAuthenticated) {
            router.push("/auth");
            return;
        }
        onItemClick?.(item.title);
    };

    return (
        <Sidebar collapsible="icon" {...props}>
            <SidebarContent>
                <SidebarGroup>
                    <SidebarGroupLabel>
                        <Image
                            src="/favicon.png"
                            alt="KeboolaDeepWiki"
                            width={24}
                            height={24}
                            className="shrink-0 rounded"
                        />
                        <span className="ml-2">KeboolaDeepWiki</span>
                    </SidebarGroupLabel>
                    <SidebarGroupContent>
                        <SidebarMenu>
                            {items.map((item) => (
                                <SidebarMenuItem key={item.title}>
                                    <SidebarMenuButton
                                        asChild
                                        tooltip={item.title}
                                        isActive={activeItem === item.title}
                                        onClick={(e) => {
                                            if (item.requireAuth && !isAuthenticated) {
                                                e.preventDefault();
                                                handleItemClick(item);
                                            } else {
                                                onItemClick?.(item.title);
                                            }
                                        }}
                                    >
                                        <Link href={item.requireAuth && !isAuthenticated ? "#" : item.url}>
                                            <item.icon />
                                            <span>{item.title}</span>
                                        </Link>
                                    </SidebarMenuButton>
                                </SidebarMenuItem>
                            ))}
                        </SidebarMenu>
                    </SidebarGroupContent>
                </SidebarGroup>
            </SidebarContent>
            <SidebarFooter>
                <SidebarMenu>
                    <SidebarMenuItem>
                        <SidebarMenuButton asChild tooltip={t("sidebar.github")}>
                            <Link href="https://github.com/keboola/OpenDeepWiki" target="_blank">
                                <GithubIcon />
                                <span>{t("sidebar.github")}</span>
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