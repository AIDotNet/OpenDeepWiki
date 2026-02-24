"use client";

import { useState, useEffect } from "react";
import {
  getMcpProviders,
  createMcpProvider,
  updateMcpProvider,
  deleteMcpProvider,
  getMcpUsageLogs,
  getModelConfigs,
  type McpProvider,
  type McpProviderRequest,
  type McpUsageLog,
  type PagedResult,
  type ModelConfig,
} from "@/lib/admin-api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  Plus,
  Pencil,
  Trash2,
  Loader2,
  Globe,
  Key,
  Cpu,
  ExternalLink,
} from "lucide-react";
import { useTranslations } from "@/hooks/use-translations";

const REPOSITORY_SCOPED_MCP_PATH_TEMPLATE = "/api/mcp/{owner}/{repo}";

const defaultFormData: McpProviderRequest = {
  name: "",
  description: "",
  serverUrl: REPOSITORY_SCOPED_MCP_PATH_TEMPLATE,
  transportType: "streamable_http",
  requiresApiKey: true,
  apiKeyObtainUrl: "",
  systemApiKey: "",
  modelConfigId: "",
  isActive: true,
  sortOrder: 0,
  iconUrl: "",
  maxRequestsPerDay: 0,
};

export default function AdminMcpProvidersPage() {
  const t = useTranslations();
  const [providers, setProviders] = useState<McpProvider[]>([]);
  const [models, setModels] = useState<ModelConfig[]>([]);
  const [loading, setLoading] = useState(true);
  const [showDialog, setShowDialog] = useState(false);
  const [editingProvider, setEditingProvider] = useState<McpProvider | null>(null);
  const [deleteId, setDeleteId] = useState<string | null>(null);
  const [formData, setFormData] = useState<McpProviderRequest>(defaultFormData);

  // Usage logs state
  const [usageLogs, setUsageLogs] = useState<PagedResult<McpUsageLog> | null>(null);
  const [logsPage, setLogsPage] = useState(1);
  const [logsLoading, setLogsLoading] = useState(false);

  useEffect(() => {
    loadData();
  }, []);

  async function loadData() {
    setLoading(true);
    try {
      const [providerData, modelData] = await Promise.all([
        getMcpProviders(),
        getModelConfigs(),
      ]);
      setProviders(providerData);
      setModels(modelData);
    } catch (error) {
      console.error("Failed to load data:", error);
    } finally {
      setLoading(false);
    }
  }

  async function loadUsageLogs(page: number = 1) {
    setLogsLoading(true);
    try {
      const result = await getMcpUsageLogs({ page, pageSize: 20 });
      setUsageLogs(result);
      setLogsPage(page);
    } catch (error) {
      console.error("Failed to load usage logs:", error);
    } finally {
      setLogsLoading(false);
    }
  }

  function openCreateDialog() {
    setEditingProvider(null);
    setFormData(defaultFormData);
    setShowDialog(true);
  }

  function openEditDialog(provider: McpProvider) {
    setEditingProvider(provider);
    setFormData({
      name: provider.name,
      description: provider.description || "",
      serverUrl: REPOSITORY_SCOPED_MCP_PATH_TEMPLATE,
      transportType: provider.transportType,
      requiresApiKey: provider.requiresApiKey,
      apiKeyObtainUrl: provider.apiKeyObtainUrl || "",
      systemApiKey: "",
      modelConfigId: provider.modelConfigId || "",
      isActive: provider.isActive,
      sortOrder: provider.sortOrder,
      iconUrl: provider.iconUrl || "",
      maxRequestsPerDay: provider.maxRequestsPerDay,
    });
    setShowDialog(true);
  }

  async function handleSave() {
    try {
      if (editingProvider) {
        await updateMcpProvider(editingProvider.id, formData);
      } else {
        await createMcpProvider(formData);
      }
      setShowDialog(false);
      await loadData();
    } catch (error) {
      console.error("Failed to save:", error);
    }
  }

  async function handleDelete() {
    if (!deleteId) return;
    try {
      await deleteMcpProvider(deleteId);
      setDeleteId(null);
      await loadData();
    } catch (error) {
      console.error("Failed to delete:", error);
    }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">{t("admin.mcpProviders.title")}</h1>
          <p className="text-muted-foreground">{t("admin.mcpProviders.description")}</p>
        </div>
        <Button onClick={openCreateDialog}>
          <Plus className="h-4 w-4 mr-2" />
          {t("admin.mcpProviders.create")}
        </Button>
      </div>

      <Tabs defaultValue="providers" onValueChange={(v) => v === "logs" && loadUsageLogs()}>
        <TabsList>
          <TabsTrigger value="providers">{t("admin.mcpProviders.providersTab")}</TabsTrigger>
          <TabsTrigger value="logs">{t("admin.mcpProviders.logsTab")}</TabsTrigger>
        </TabsList>

        <TabsContent value="providers" className="space-y-4">
          {providers.length === 0 ? (
            <Card>
              <CardContent className="flex flex-col items-center justify-center py-12">
                <Globe className="h-12 w-12 text-muted-foreground mb-4" />
                <p className="text-muted-foreground">{t("admin.mcpProviders.empty")}</p>
              </CardContent>
            </Card>
          ) : (
            <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
              {providers.map((provider) => (
                <Card key={provider.id} className={!provider.isActive ? "opacity-60" : ""}>
                  <CardHeader className="pb-3">
                    <div className="flex items-start justify-between">
                      <div className="flex items-center gap-2">
                        {provider.iconUrl ? (
                          <img src={provider.iconUrl} alt="" className="h-6 w-6 rounded" />
                        ) : (
                          <Globe className="h-5 w-5 text-muted-foreground" />
                        )}
                        <CardTitle className="text-lg">{provider.name}</CardTitle>
                      </div>
                      <div className="flex gap-1">
                        <Button variant="ghost" size="icon" onClick={() => openEditDialog(provider)}>
                          <Pencil className="h-4 w-4" />
                        </Button>
                        <Button variant="ghost" size="icon" onClick={() => setDeleteId(provider.id)}>
                          <Trash2 className="h-4 w-4 text-destructive" />
                        </Button>
                      </div>
                    </div>
                    {provider.description && (
                      <CardDescription>{provider.description}</CardDescription>
                    )}
                  </CardHeader>
                  <CardContent className="space-y-2 text-sm">
                    <div className="flex items-center gap-2">
                      <Globe className="h-3.5 w-3.5 text-muted-foreground" />
                      <span className="text-muted-foreground truncate">{provider.serverUrl}</span>
                    </div>
                    <div className="flex flex-wrap gap-1.5">
                      <Badge variant="secondary">{provider.transportType}</Badge>
                      {provider.requiresApiKey && (
                        <Badge variant="outline" className="gap-1">
                          <Key className="h-3 w-3" />
                          API Key
                        </Badge>
                      )}
                      {provider.modelConfigName && (
                        <Badge variant="outline" className="gap-1">
                          <Cpu className="h-3 w-3" />
                          {provider.modelConfigName}
                        </Badge>
                      )}
                      {!provider.isActive && <Badge variant="destructive">Disabled</Badge>}
                    </div>
                    {provider.apiKeyObtainUrl && (
                      <a
                        href={provider.apiKeyObtainUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="flex items-center gap-1 text-xs text-primary hover:underline"
                      >
                        <ExternalLink className="h-3 w-3" />
                        {t("admin.mcpProviders.getApiKey")}
                      </a>
                    )}
                    {provider.maxRequestsPerDay > 0 && (
                      <p className="text-xs text-muted-foreground">
                        {t("admin.mcpProviders.dailyLimit")}: {provider.maxRequestsPerDay}
                      </p>
                    )}
                  </CardContent>
                </Card>
              ))}
            </div>
          )}
        </TabsContent>

        <TabsContent value="logs">
          {logsLoading ? (
            <div className="flex items-center justify-center h-32">
              <Loader2 className="h-6 w-6 animate-spin" />
            </div>
          ) : usageLogs && usageLogs.items.length > 0 ? (
            <div className="space-y-4">
              <div className="rounded-md border overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="border-b bg-muted/50">
                    <tr>
                      <th className="px-4 py-3 text-left font-medium">{t("admin.mcpProviders.logTime")}</th>
                      <th className="px-4 py-3 text-left font-medium">{t("admin.mcpProviders.logUser")}</th>
                      <th className="px-4 py-3 text-left font-medium">{t("admin.mcpProviders.logTool")}</th>
                      <th className="px-4 py-3 text-left font-medium">{t("admin.mcpProviders.logStatus")}</th>
                      <th className="px-4 py-3 text-left font-medium">{t("admin.mcpProviders.logDuration")}</th>
                      <th className="px-4 py-3 text-left font-medium">IP</th>
                    </tr>
                  </thead>
                  <tbody>
                    {usageLogs.items.map((log) => (
                      <tr key={log.id} className="border-b last:border-0">
                        <td className="px-4 py-3 text-xs">
                          {new Date(log.createdAt).toLocaleString()}
                        </td>
                        <td className="px-4 py-3">{log.userName || log.userId || "-"}</td>
                        <td className="px-4 py-3 font-mono text-xs">{log.toolName}</td>
                        <td className="px-4 py-3">
                          <Badge variant={log.responseStatus < 400 ? "secondary" : "destructive"}>
                            {log.responseStatus}
                          </Badge>
                        </td>
                        <td className="px-4 py-3">{log.durationMs}ms</td>
                        <td className="px-4 py-3 text-xs text-muted-foreground">
                          {log.ipAddress || "-"}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="flex justify-between items-center">
                <p className="text-sm text-muted-foreground">
                  {t("admin.mcpProviders.logTotal")}: {usageLogs.total}
                </p>
                <div className="flex gap-2">
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={logsPage <= 1}
                    onClick={() => loadUsageLogs(logsPage - 1)}
                  >
                    {t("common.previous")}
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={logsPage * 20 >= usageLogs.total}
                    onClick={() => loadUsageLogs(logsPage + 1)}
                  >
                    {t("common.next")}
                  </Button>
                </div>
              </div>
            </div>
          ) : (
            <Card>
              <CardContent className="flex flex-col items-center justify-center py-12">
                <p className="text-muted-foreground">{t("admin.mcpProviders.noLogs")}</p>
              </CardContent>
            </Card>
          )}
        </TabsContent>
      </Tabs>

      {/* Create/Edit Dialog */}
      <Dialog open={showDialog} onOpenChange={setShowDialog}>
        <DialogContent className="max-w-2xl max-h-[90vh] overflow-y-auto">
          <DialogHeader>
            <DialogTitle>
              {editingProvider
                ? t("admin.mcpProviders.edit")
                : t("admin.mcpProviders.create")}
            </DialogTitle>
            <DialogDescription>
              {t("admin.mcpProviders.dialogDescription")}
            </DialogDescription>
          </DialogHeader>
          <div className="grid gap-4 py-4">
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label>{t("admin.mcpProviders.fieldName")}</Label>
                <Input
                  value={formData.name}
                  onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                  placeholder=" MCP"
                />
              </div>
              <div className="space-y-2">
                <Label>{t("admin.mcpProviders.fieldServerUrl")}</Label>
                <Input
                  value={REPOSITORY_SCOPED_MCP_PATH_TEMPLATE}
                  readOnly
                />
                <p className="text-xs text-muted-foreground">
                  固定路径模板，后端会按仓库解析 owner/repo。
                </p>
              </div>
            </div>

            <div className="space-y-2">
              <Label>{t("admin.mcpProviders.fieldDescription")}</Label>
              <Textarea
                value={formData.description || ""}
                onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                rows={2}
              />
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label>{t("admin.mcpProviders.fieldTransportType")}</Label>
                <Select
                  value={formData.transportType}
                  onValueChange={(v) => setFormData({ ...formData, transportType: v })}
                >
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="streamable_http">Streamable HTTP</SelectItem>
                    <SelectItem value="sse">SSE</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <Label>{t("admin.mcpProviders.fieldModel")}</Label>
                <Select
                  value={formData.modelConfigId || "none"}
                  onValueChange={(v) =>
                    setFormData({ ...formData, modelConfigId: v === "none" ? "" : v })
                  }
                >
                  <SelectTrigger>
                    <SelectValue placeholder={t("admin.mcpProviders.noModel")} />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="none">{t("admin.mcpProviders.noModel")}</SelectItem>
                    {models
                      .filter((m) => m.isActive)
                      .map((m) => (
                        <SelectItem key={m.id} value={m.id}>
                          {m.name}
                        </SelectItem>
                      ))}
                  </SelectContent>
                </Select>
              </div>
            </div>

            <div className="space-y-4 rounded-lg border p-4">
              <div className="flex items-center justify-between">
                <div>
                  <Label>{t("admin.mcpProviders.fieldRequiresApiKey")}</Label>
                  <p className="text-xs text-muted-foreground">
                    {t("admin.mcpProviders.fieldRequiresApiKeyHint")}
                  </p>
                </div>
                <Switch
                  checked={formData.requiresApiKey}
                  onCheckedChange={(v) => setFormData({ ...formData, requiresApiKey: v })}
                />
              </div>
              {formData.requiresApiKey && (
                <div className="space-y-2">
                  <Label>{t("admin.mcpProviders.fieldApiKeyObtainUrl")}</Label>
                  <Input
                    value={formData.apiKeyObtainUrl || ""}
                    onChange={(e) => setFormData({ ...formData, apiKeyObtainUrl: e.target.value })}
                    placeholder="https://platform.example.com/api-keys"
                  />
                </div>
              )}
              {!formData.requiresApiKey && (
                <div className="space-y-2">
                  <Label>{t("admin.mcpProviders.fieldSystemApiKey")}</Label>
                  <Input
                    type="password"
                    value={formData.systemApiKey || ""}
                    onChange={(e) => setFormData({ ...formData, systemApiKey: e.target.value })}
                    placeholder={editingProvider?.hasSystemApiKey ? "••••••••" : ""}
                  />
                </div>
              )}
            </div>

            <div className="grid grid-cols-3 gap-4">
              <div className="space-y-2">
                <Label>{t("admin.mcpProviders.fieldSortOrder")}</Label>
                <Input
                  type="number"
                  value={formData.sortOrder}
                  onChange={(e) =>
                    setFormData({ ...formData, sortOrder: parseInt(e.target.value) || 0 })
                  }
                />
              </div>
              <div className="space-y-2">
                <Label>{t("admin.mcpProviders.fieldDailyLimit")}</Label>
                <Input
                  type="number"
                  value={formData.maxRequestsPerDay}
                  onChange={(e) =>
                    setFormData({
                      ...formData,
                      maxRequestsPerDay: parseInt(e.target.value) || 0,
                    })
                  }
                />
                <p className="text-xs text-muted-foreground">0 = {t("admin.mcpProviders.unlimited")}</p>
              </div>
              <div className="space-y-2">
                <Label>{t("admin.mcpProviders.fieldIconUrl")}</Label>
                <Input
                  value={formData.iconUrl || ""}
                  onChange={(e) => setFormData({ ...formData, iconUrl: e.target.value })}
                  placeholder="https://..."
                />
              </div>
            </div>

            <div className="flex items-center gap-2">
              <Switch
                checked={formData.isActive}
                onCheckedChange={(v) => setFormData({ ...formData, isActive: v })}
              />
              <Label>{t("admin.mcpProviders.fieldIsActive")}</Label>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowDialog(false)}>
              {t("common.cancel")}
            </Button>
            <Button onClick={handleSave} disabled={!formData.name}>
              {t("common.save")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Delete Confirmation */}
      <AlertDialog open={!!deleteId} onOpenChange={() => setDeleteId(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t("admin.mcpProviders.deleteTitle")}</AlertDialogTitle>
            <AlertDialogDescription>
              {t("admin.mcpProviders.deleteDescription")}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t("common.cancel")}</AlertDialogCancel>
            <AlertDialogAction onClick={handleDelete} className="bg-destructive text-destructive-foreground">
              {t("common.delete")}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
