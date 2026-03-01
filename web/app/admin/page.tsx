"use client";

import { useEffect, useState } from "react";
import { Card } from "@/components/ui/card";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  getDashboardStatistics,
  getTokenUsageStatistics,
  getMcpUsageStatistics,
  DashboardStatistics,
  TokenUsageStatistics,
  McpUsageStatistics,
} from "@/lib/admin-api";
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  Legend,
  ResponsiveContainer,
} from "recharts";
import { Loader2, GitBranch, Users, Coins, TrendingUp, Globe } from "lucide-react";
import { useTranslations } from "@/hooks/use-translations";
import { useLocale } from "next-intl";

// 自定义 Tooltip 组件
interface TooltipPayload {
  name: string;
  value: number;
  color: string;
}

interface CustomTooltipProps {
  active?: boolean;
  payload?: TooltipPayload[];
  label?: string;
  totalLabel?: string;
  valueFormatter?: (value: number) => string;
}

function formatNumberWithUnits(value: number) {
  const abs = Math.abs(value);
  const sign = value < 0 ? "-" : "";

  const formatCompact = (num: number, unit: "K" | "M" | "B") => {
    const fixed = num >= 100 ? num.toFixed(0) : num.toFixed(1);
    return `${sign}${fixed.replace(/\\.0$/, "")}${unit}`;
  };

  if (abs >= 1_000_000_000) return formatCompact(abs / 1_000_000_000, "B");
  if (abs >= 1_000_000) return formatCompact(abs / 1_000_000, "M");
  if (abs >= 1_000) return formatCompact(abs / 1_000, "K");

  return value.toLocaleString();
}

function CustomTooltip({
  active,
  payload,
  label,
  totalLabel,
  valueFormatter,
}: CustomTooltipProps) {
  if (!active || !payload?.length) return null;
  const formatValue = valueFormatter ?? ((value: number) => value.toLocaleString());

  return (
    <div className="rounded-xl border bg-background/95 backdrop-blur-sm p-4 shadow-xl">
      <p className="text-sm font-medium text-muted-foreground mb-3">{label}</p>
      <div className="space-y-2">
        {payload.map((entry, index: number) => (
          <div key={index} className="flex items-center justify-between gap-8">
            <div className="flex items-center gap-2">
              <span
                className="h-3 w-3 rounded-full"
                style={{ backgroundColor: entry.color }}
              />
              <span className="text-sm text-muted-foreground">{entry.name}</span>
            </div>
            <span className="text-sm font-semibold tabular-nums">
              {formatValue(entry.value ?? 0)}
            </span>
          </div>
        ))}
      </div>
      {payload.length > 1 && (
        <div className="mt-3 pt-3 border-t flex items-center justify-between">
          <span className="text-sm text-muted-foreground">{totalLabel}</span>
          <span className="text-sm font-bold tabular-nums">
            {formatValue(payload.reduce((sum: number, entry) => sum + (entry.value ?? 0), 0))}
          </span>
        </div>
      )}
    </div>
  );
}

export default function AdminDashboardPage() {
  const [dashboardStats, setDashboardStats] = useState<DashboardStatistics | null>(null);
  const [tokenStats, setTokenStats] = useState<TokenUsageStatistics | null>(null);
  const [mcpStats, setMcpStats] = useState<McpUsageStatistics | null>(null);
  const [loading, setLoading] = useState(true);
  const [days, setDays] = useState(7);
  const t = useTranslations();
  const locale = useLocale();

  useEffect(() => {
    async function fetchData() {
      setLoading(true);
      try {
        const [dashboard, token] = await Promise.all([
          getDashboardStatistics(days),
          getTokenUsageStatistics(days),
        ]);
        setDashboardStats(dashboard);
        setTokenStats(token);
        try {
          const mcp = await getMcpUsageStatistics(days);
          setMcpStats(mcp);
        } catch {
          // MCP stats may not be available if MCP is not configured
        }
      } catch (error) {
        console.error("Failed to fetch statistics:", error);
      } finally {
        setLoading(false);
      }
    }
    fetchData();
  }, [days]);

  if (loading) {
    return (
      <div className="flex h-[50vh] items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  const repoChartData = dashboardStats?.repositoryStats.map((stat) => ({
    date: new Date(stat.date).toLocaleDateString(locale === 'zh' ? 'zh-CN' : locale, { month: "short", day: "numeric" }),
    [t('admin.dashboard.submitCount')]: stat.submittedCount,
    [t('admin.dashboard.processed')]: stat.processedCount,
  })) || [];

  const userChartData = dashboardStats?.userStats.map((stat) => ({
    date: new Date(stat.date).toLocaleDateString(locale === 'zh' ? 'zh-CN' : locale, { month: "short", day: "numeric" }),
    [t('admin.dashboard.newUsers')]: stat.newUserCount,
  })) || [];

  const tokenChartData = tokenStats?.dailyUsages.map((stat) => ({
    date: new Date(stat.date).toLocaleDateString(locale === 'zh' ? 'zh-CN' : locale, { month: "short", day: "numeric" }),
    [t('admin.dashboard.inputToken')]: stat.inputTokens,
    [t('admin.dashboard.outputToken')]: stat.outputTokens,
  })) || [];

  const totalRepoSubmitted = dashboardStats?.repositoryStats.reduce((sum, s) => sum + s.submittedCount, 0) || 0;
  const totalRepoProcessed = dashboardStats?.repositoryStats.reduce((sum, s) => sum + s.processedCount, 0) || 0;
  const totalNewUsers = dashboardStats?.userStats.reduce((sum, s) => sum + s.newUserCount, 0) || 0;

  const mcpChartData = mcpStats?.dailyUsages.map((stat) => ({
    date: new Date(stat.date).toLocaleDateString(locale === 'zh' ? 'zh-CN' : locale, { month: "short", day: "numeric" }),
    [t('admin.dashboard.mcpRequests')]: stat.requestCount,
    [t('admin.dashboard.mcpErrors')]: stat.errorCount,
  })) || [];

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">{t('admin.dashboard.title')}</h1>
        <Tabs value={days.toString()} onValueChange={(v) => setDays(parseInt(v))}>
          <TabsList>
            <TabsTrigger value="7">{t('admin.dashboard.days7')}</TabsTrigger>
            <TabsTrigger value="14">{t('admin.dashboard.days14')}</TabsTrigger>
            <TabsTrigger value="30">{t('admin.dashboard.days30')}</TabsTrigger>
          </TabsList>
        </Tabs>
      </div>

      {/* 统计卡片 */}
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-5">
        <Card className="p-6">
          <div className="flex items-center gap-4">
            <div className="rounded-full bg-blue-100 p-3 dark:bg-blue-900">
              <GitBranch className="h-6 w-6 text-blue-600 dark:text-blue-400" />
            </div>
            <div>
              <p className="text-sm text-muted-foreground">{t('admin.dashboard.repoSubmit')}</p>
              <p className="text-2xl font-bold">{totalRepoSubmitted}</p>
            </div>
          </div>
        </Card>
        <Card className="p-6">
          <div className="flex items-center gap-4">
            <div className="rounded-full bg-green-100 p-3 dark:bg-green-900">
              <TrendingUp className="h-6 w-6 text-green-600 dark:text-green-400" />
            </div>
            <div>
              <p className="text-sm text-muted-foreground">{t('admin.dashboard.processed')}</p>
              <p className="text-2xl font-bold">{totalRepoProcessed}</p>
            </div>
          </div>
        </Card>
        <Card className="p-6">
          <div className="flex items-center gap-4">
            <div className="rounded-full bg-purple-100 p-3 dark:bg-purple-900">
              <Users className="h-6 w-6 text-purple-600 dark:text-purple-400" />
            </div>
            <div>
              <p className="text-sm text-muted-foreground">{t('admin.dashboard.newUsers')}</p>
              <p className="text-2xl font-bold">{totalNewUsers}</p>
            </div>
          </div>
        </Card>
        <Card className="p-6">
          <div className="flex items-center gap-4">
            <div className="rounded-full bg-orange-100 p-3 dark:bg-orange-900">
              <Coins className="h-6 w-6 text-orange-600 dark:text-orange-400" />
            </div>
            <div>
              <p className="text-sm text-muted-foreground">{t('admin.dashboard.tokenUsage')}</p>
              <p className="text-2xl font-bold">
                {formatNumberWithUnits(tokenStats?.totalTokens || 0)}
              </p>
            </div>
          </div>
        </Card>
        <Card className="p-6">
          <div className="flex items-center gap-4">
            <div className="rounded-full bg-cyan-100 p-3 dark:bg-cyan-900">
              <Globe className="h-6 w-6 text-cyan-600 dark:text-cyan-400" />
            </div>
            <div>
              <p className="text-sm text-muted-foreground">{t('admin.dashboard.mcpTotal')}</p>
              <p className="text-2xl font-bold">{(mcpStats?.totalRequests || 0).toLocaleString()}</p>
            </div>
          </div>
        </Card>
      </div>

      {/* 图表区域 */}
      <div className="grid gap-6 lg:grid-cols-2">
        {/* 仓库统计图表 */}
        <Card className="p-6">
          <h3 className="mb-4 text-lg font-semibold">{t('admin.dashboard.repoStats')}</h3>
          <ResponsiveContainer width="100%" height={300}>
            <BarChart data={repoChartData} barCategoryGap="20%">
              <XAxis
                dataKey="date"
                axisLine={false}
                tickLine={false}
                tick={{ fill: '#888', fontSize: 12 }}
              />
              <YAxis
                axisLine={false}
                tickLine={false}
                tick={{ fill: '#888', fontSize: 12 }}
              />
              <Tooltip
                cursor={{ fill: 'rgba(0,0,0,0.04)' }}
                content={<CustomTooltip totalLabel={t('admin.dashboard.total')} />}
              />
              <Legend wrapperStyle={{ paddingTop: 16 }} />
              <Bar dataKey={t('admin.dashboard.submitCount')} fill="#3b82f6" radius={[4, 4, 0, 0]} />
              <Bar dataKey={t('admin.dashboard.processed')} fill="#22c55e" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </Card>

        {/* 用户增长图表 */}
        <Card className="p-6">
          <h3 className="mb-4 text-lg font-semibold">{t('admin.dashboard.userGrowth')}</h3>
          <ResponsiveContainer width="100%" height={300}>
            <BarChart data={userChartData} barCategoryGap="30%">
              <XAxis
                dataKey="date"
                axisLine={false}
                tickLine={false}
                tick={{ fill: '#888', fontSize: 12 }}
              />
              <YAxis
                axisLine={false}
                tickLine={false}
                tick={{ fill: '#888', fontSize: 12 }}
              />
              <Tooltip
                cursor={{ fill: 'rgba(0,0,0,0.04)' }}
                content={<CustomTooltip totalLabel={t('admin.dashboard.total')} />}
              />
              <Legend wrapperStyle={{ paddingTop: 16 }} />
              <Bar dataKey={t('admin.dashboard.newUsers')} fill="#8b5cf6" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </Card>

        {/* MCP 使用图表 */}
        <Card className="p-6">
          <h3 className="mb-4 text-lg font-semibold">{t('admin.dashboard.mcpTrend')}</h3>
          <ResponsiveContainer width="100%" height={300}>
            <BarChart data={mcpChartData} barCategoryGap="20%">
              <XAxis
                dataKey="date"
                axisLine={false}
                tickLine={false}
                tick={{ fill: '#888', fontSize: 12 }}
              />
              <YAxis
                axisLine={false}
                tickLine={false}
                tick={{ fill: '#888', fontSize: 12 }}
              />
              <Tooltip
                cursor={{ fill: 'rgba(0,0,0,0.04)' }}
                content={<CustomTooltip totalLabel={t('admin.dashboard.total')} />}
              />
              <Legend wrapperStyle={{ paddingTop: 16 }} />
              <Bar dataKey={t('admin.dashboard.mcpRequests')} fill="#06b6d4" radius={[4, 4, 0, 0]} />
              <Bar dataKey={t('admin.dashboard.mcpErrors')} fill="#ef4444" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </Card>

        {/* Token 消耗图表 */}
        <Card className="p-6 lg:col-span-2">
          <h3 className="mb-4 text-lg font-semibold">{t('admin.dashboard.tokenTrend')}</h3>
          <ResponsiveContainer width="100%" height={300}>
            <BarChart data={tokenChartData} barCategoryGap="20%">
              <XAxis
                dataKey="date"
                axisLine={false}
                tickLine={false}
                tick={{ fill: '#888', fontSize: 12 }}
              />
              <YAxis
                axisLine={false}
                tickLine={false}
                tick={{ fill: '#888', fontSize: 12 }}
                tickFormatter={formatNumberWithUnits}
              />
              <Tooltip
                cursor={{ fill: 'rgba(0,0,0,0.04)' }}
                content={
                  <CustomTooltip
                    totalLabel={t('admin.dashboard.total')}
                    valueFormatter={formatNumberWithUnits}
                  />
                }
              />
              <Legend wrapperStyle={{ paddingTop: 16 }} />
              <Bar dataKey={t('admin.dashboard.inputToken')} fill="#f97316" radius={[4, 4, 0, 0]} />
              <Bar dataKey={t('admin.dashboard.outputToken')} fill="#eab308" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </Card>
      </div>
    </div>
  );
}
