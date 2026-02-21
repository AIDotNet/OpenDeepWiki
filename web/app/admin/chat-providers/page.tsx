"use client";

import React, { useCallback, useEffect, useMemo, useState } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
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
import {
  getChatProviderConfigs,
  getChatProviderConfig,
  saveChatProviderConfig,
  deleteChatProviderConfig,
  enableChatProvider,
  disableChatProvider,
  reloadChatProviderConfig,
  ChatProviderStatus,
  ChatProviderConfig,
} from "@/lib/admin-api";
import {
  Loader2,
  Trash2,
  Edit,
  RefreshCw,
  Plus,
  MessageSquare,
  CheckCircle,
  XCircle,
  RotateCcw,
} from "lucide-react";
import { toast } from "sonner";
import { useTranslations } from "@/hooks/use-translations";

const PLATFORM_OPTIONS = [
  { value: "feishu", labelKey: "admin.chatProviders.platformFeishu" },
  { value: "qq", labelKey: "admin.chatProviders.platformQq" },
  { value: "wechat", labelKey: "admin.chatProviders.platformWechat" },
  { value: "slack", labelKey: "admin.chatProviders.platformSlack" },
  { value: "custom", labelKey: "admin.chatProviders.platformCustom" },
];

type FieldType = "text" | "password" | "number" | "switch";

interface PlatformFieldDefinition {
  key: string;
  labelKey: string;
  type: FieldType;
  required?: boolean;
}

const PLATFORM_FIELDS: Record<string, PlatformFieldDefinition[]> = {
  feishu: [
    { key: "AppId", labelKey: "admin.chatProviders.fieldAppId", type: "text", required: true },
    { key: "AppSecret", labelKey: "admin.chatProviders.fieldAppSecret", type: "password", required: true },
    { key: "VerificationToken", labelKey: "admin.chatProviders.fieldVerificationToken", type: "password" },
    { key: "EncryptKey", labelKey: "admin.chatProviders.fieldEncryptKey", type: "password" },
    { key: "ApiBaseUrl", labelKey: "admin.chatProviders.fieldApiBaseUrl", type: "text" },
    { key: "TokenCacheSeconds", labelKey: "admin.chatProviders.fieldTokenCacheSeconds", type: "number" },
  ],
  qq: [
    { key: "AppId", labelKey: "admin.chatProviders.fieldAppId", type: "text", required: true },
    { key: "AppSecret", labelKey: "admin.chatProviders.fieldAppSecret", type: "password" },
    { key: "Token", labelKey: "admin.chatProviders.fieldToken", type: "password", required: true },
    { key: "ApiBaseUrl", labelKey: "admin.chatProviders.fieldApiBaseUrl", type: "text" },
    { key: "SandboxApiBaseUrl", labelKey: "admin.chatProviders.fieldSandboxApiBaseUrl", type: "text" },
    { key: "UseSandbox", labelKey: "admin.chatProviders.fieldUseSandbox", type: "switch" },
    { key: "TokenCacheSeconds", labelKey: "admin.chatProviders.fieldTokenCacheSeconds", type: "number" },
    { key: "HeartbeatInterval", labelKey: "admin.chatProviders.fieldHeartbeatInterval", type: "number" },
    { key: "ReconnectInterval", labelKey: "admin.chatProviders.fieldReconnectInterval", type: "number" },
    { key: "MaxReconnectAttempts", labelKey: "admin.chatProviders.fieldMaxReconnectAttempts", type: "number" },
  ],
  wechat: [
    { key: "AppId", labelKey: "admin.chatProviders.fieldAppId", type: "text", required: true },
    { key: "AppSecret", labelKey: "admin.chatProviders.fieldAppSecret", type: "password", required: true },
    { key: "Token", labelKey: "admin.chatProviders.fieldToken", type: "password", required: true },
    { key: "EncodingAesKey", labelKey: "admin.chatProviders.fieldEncodingAesKey", type: "password", required: true },
    { key: "ApiBaseUrl", labelKey: "admin.chatProviders.fieldApiBaseUrl", type: "text" },
    { key: "TokenCacheSeconds", labelKey: "admin.chatProviders.fieldTokenCacheSeconds", type: "number" },
    { key: "EncryptMode", labelKey: "admin.chatProviders.fieldEncryptMode", type: "text" },
  ],
  slack: [
    { key: "BotToken", labelKey: "admin.chatProviders.fieldBotToken", type: "password", required: true },
    { key: "SigningSecret", labelKey: "admin.chatProviders.fieldSigningSecret", type: "password", required: true },
    { key: "ApiBaseUrl", labelKey: "admin.chatProviders.fieldApiBaseUrl", type: "text" },
    { key: "ReplyInThread", labelKey: "admin.chatProviders.fieldReplyInThread", type: "switch" },
  ],
};

function parseConfigData(configData?: string) {
  if (!configData) return {} as Record<string, unknown>;
  try {
    return JSON.parse(configData) as Record<string, unknown>;
  } catch {
    return {} as Record<string, unknown>;
  }
}

export default function AdminChatProvidersPage() {
  const [configs, setConfigs] = useState<ChatProviderStatus[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [showDialog, setShowDialog] = useState(false);
  const [editingConfig, setEditingConfig] = useState<ChatProviderConfig | null>(null);
  const [deletePlatform, setDeletePlatform] = useState<string | null>(null);
  const [rawConfigData, setRawConfigData] = useState("{}");
  const [formData, setFormData] = useState({
    platform: "feishu",
    displayName: "",
    isEnabled: true,
    webhookUrl: "",
    messageInterval: 500,
    maxRetryCount: 3,
  });
  const [fieldValues, setFieldValues] = useState<Record<string, string | boolean>>({});
  const t = useTranslations();

  const platformFieldDefinitions = useMemo(() => {
    return PLATFORM_FIELDS[formData.platform] || [];
  }, [formData.platform]);

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getChatProviderConfigs();
      setConfigs(Array.isArray(result) ? result : []);
    } catch (error) {
      console.error("Failed to fetch chat provider configs:", error);
      toast.error(t("admin.toast.fetchConfigFailed"));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const resetForm = (platform?: string) => {
    const targetPlatform = platform || "feishu";
    setFormData({
      platform: targetPlatform,
      displayName: "",
      isEnabled: true,
      webhookUrl: "",
      messageInterval: 500,
      maxRetryCount: 3,
    });
    setFieldValues({});
    setRawConfigData("{}");
  };

  const buildConfigData = () => {
    if (!PLATFORM_FIELDS[formData.platform]) {
      return rawConfigData || "{}";
    }

    const payload: Record<string, unknown> = {};
    platformFieldDefinitions.forEach((field) => {
      const value = fieldValues[field.key];
      if (value === undefined || value === "") {
        return;
      }
      if (field.type === "number") {
        const parsed = Number(value);
        if (!Number.isNaN(parsed)) {
          payload[field.key] = parsed;
        }
        return;
      }
      payload[field.key] = value;
    });

    return JSON.stringify(payload, null, 2);
  };

  const openCreateDialog = () => {
    setEditingConfig(null);
    resetForm("feishu");
    setShowDialog(true);
  };

  const openEditDialog = async (config: ChatProviderStatus) => {
    try {
      const detail = await getChatProviderConfig(config.platform);
      setEditingConfig(detail);
      const configData = parseConfigData(detail.configData);
      setFormData({
        platform: detail.platform,
        displayName: detail.displayName,
        isEnabled: detail.isEnabled,
        webhookUrl: detail.webhookUrl || "",
        messageInterval: detail.messageInterval || 0,
        maxRetryCount: detail.maxRetryCount || 0,
      });
      setRawConfigData(detail.configData || "{}");
      const nextFieldValues: Record<string, string | boolean> = {};
      (PLATFORM_FIELDS[detail.platform] || []).forEach((field) => {
        const fieldValue = configData[field.key];
        if (field.type === "switch") {
          nextFieldValues[field.key] = Boolean(fieldValue);
        } else if (fieldValue !== undefined && fieldValue !== null) {
          nextFieldValues[field.key] = String(fieldValue);
        }
      });
      setFieldValues(nextFieldValues);
      setShowDialog(true);
    } catch (error) {
      toast.error(t("admin.toast.fetchDetailFailed"));
    }
  };

  const handleSave = async () => {
    if (!formData.platform.trim() || !formData.displayName.trim()) {
      toast.error(t("admin.toast.fillRequired"));
      return;
    }

    const requiredFields = platformFieldDefinitions.filter((field) => field.required);
    const missingRequired = requiredFields.some((field) => {
      const value = fieldValues[field.key];
      return value === undefined || value === "" || value === false;
    });

    if (missingRequired) {
      toast.error(t("admin.toast.fillRequired"));
      return;
    }

    setSaving(true);
    try {
      const configPayload: ChatProviderConfig = {
        platform: formData.platform,
        displayName: formData.displayName,
        isEnabled: formData.isEnabled,
        webhookUrl: formData.webhookUrl || undefined,
        messageInterval: Number(formData.messageInterval) || 0,
        maxRetryCount: Number(formData.maxRetryCount) || 0,
        configData: buildConfigData(),
      };

      await saveChatProviderConfig(configPayload);
      toast.success(t("admin.toast.configSaveSuccess"));
      setShowDialog(false);
      fetchData();
    } catch (error) {
      toast.error(t("admin.toast.configSaveFailed"));
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async () => {
    if (!deletePlatform) return;
    try {
      await deleteChatProviderConfig(deletePlatform);
      toast.success(t("admin.toast.deleteSuccess"));
      setDeletePlatform(null);
      fetchData();
    } catch (error) {
      toast.error(t("admin.toast.deleteFailed"));
    }
  };

  const handleToggleEnabled = async (config: ChatProviderStatus) => {
    try {
      if (config.isEnabled) {
        await disableChatProvider(config.platform);
        toast.success(t("admin.chatProviders.disabled"));
      } else {
        await enableChatProvider(config.platform);
        toast.success(t("admin.chatProviders.enabled"));
      }
      fetchData();
    } catch (error) {
      toast.error(t("admin.toast.operationFailed"));
    }
  };

  const handleReload = async (config: ChatProviderStatus) => {
    try {
      await reloadChatProviderConfig(config.platform);
      toast.success(t("admin.toast.refreshSuccess"));
    } catch (error) {
      toast.error(t("admin.toast.refreshFailed"));
    }
  };

  const renderPlatformFields = () => {
    if (!PLATFORM_FIELDS[formData.platform]) {
      return (
        <div>
          <label className="text-sm font-medium">{t("admin.chatProviders.configJson")}</label>
          <Textarea
            value={rawConfigData}
            onChange={(event) => setRawConfigData(event.target.value)}
            rows={6}
            className="font-mono text-sm"
          />
        </div>
      );
    }

    return (
      <div className="space-y-4">
        {platformFieldDefinitions.map((field) => {
          const value = fieldValues[field.key];
          if (field.type === "switch") {
            return (
              <div key={field.key} className="flex items-center justify-between rounded-md border px-3 py-2">
                <label className="text-sm font-medium">
                  {t(field.labelKey)}{field.required ? " *" : ""}
                </label>
                <Switch
                  checked={Boolean(value)}
                  onCheckedChange={(checked) =>
                    setFieldValues((prev) => ({ ...prev, [field.key]: checked }))
                  }
                />
              </div>
            );
          }

          return (
            <div key={field.key}>
              <label className="text-sm font-medium">
                {t(field.labelKey)}{field.required ? " *" : ""}
              </label>
              <Input
                type={field.type}
                value={typeof value === "string" ? value : ""}
                onChange={(event) =>
                  setFieldValues((prev) => ({ ...prev, [field.key]: event.target.value }))
                }
              />
            </div>
          );
        })}
      </div>
    );
  };

  const platformLabel = (platform: string) => {
    const option = PLATFORM_OPTIONS.find((item) => item.value === platform);
    return option ? t(option.labelKey) : platform;
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <MessageSquare className="h-6 w-6" />
          <h1 className="text-2xl font-bold">{t("admin.chatProviders.title")}</h1>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" onClick={fetchData}>
            <RefreshCw className="mr-2 h-4 w-4" />
            {t("admin.common.refresh")}
          </Button>
          <Button onClick={openCreateDialog}>
            <Plus className="mr-2 h-4 w-4" />
            {t("admin.chatProviders.create")}
          </Button>
        </div>
      </div>

      {loading ? (
        <div className="flex h-64 items-center justify-center">
          <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
        </div>
      ) : configs.length === 0 ? (
        <Card className="flex h-64 items-center justify-center">
          <div className="text-center">
            <MessageSquare className="mx-auto h-12 w-12 text-muted-foreground" />
            <p className="mt-4 text-muted-foreground">{t("admin.chatProviders.empty")}</p>
            <Button className="mt-4" onClick={openCreateDialog}>
              <Plus className="mr-2 h-4 w-4" />
              {t("admin.chatProviders.addFirst")}
            </Button>
          </div>
        </Card>
      ) : (
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
          {configs.map((config) => (
            <Card key={config.platform} className="p-6">
              <div className="flex items-start justify-between">
                <div className="flex items-center gap-3">
                  <div className={`rounded-full p-2 ${config.isEnabled ? "bg-emerald-100 dark:bg-emerald-900" : "bg-gray-100 dark:bg-gray-800"}`}>
                    <MessageSquare className={`h-5 w-5 ${config.isEnabled ? "text-emerald-600 dark:text-emerald-400" : "text-gray-500"}`} />
                  </div>
                  <div>
                    <h3 className="font-semibold">{config.displayName}</h3>
                    <p className="text-xs text-muted-foreground">{platformLabel(config.platform)}</p>
                    <div className="mt-1 flex items-center gap-2 text-xs">
                      {config.isEnabled ? (
                        <span className="flex items-center gap-1 text-emerald-600">
                          <CheckCircle className="h-3 w-3" /> {t("admin.chatProviders.enabled")}
                        </span>
                      ) : (
                        <span className="flex items-center gap-1 text-gray-400">
                          <XCircle className="h-3 w-3" /> {t("admin.chatProviders.disabled")}
                        </span>
                      )}
                      {config.isRegistered ? (
                        <span className="text-xs text-blue-600">{t("admin.chatProviders.registered")}</span>
                      ) : (
                        <span className="text-xs text-amber-600">{t("admin.chatProviders.unregistered")}</span>
                      )}
                    </div>
                  </div>
                </div>
                <div className="flex gap-1">
                  <Button variant="ghost" size="icon" onClick={() => handleReload(config)}>
                    <RotateCcw className="h-4 w-4" />
                  </Button>
                  <Button variant="ghost" size="icon" onClick={() => openEditDialog(config)}>
                    <Edit className="h-4 w-4" />
                  </Button>
                  <Button variant="ghost" size="icon" onClick={() => setDeletePlatform(config.platform)}>
                    <Trash2 className="h-4 w-4 text-red-500" />
                  </Button>
                </div>
              </div>
              <div className="mt-4 space-y-2 text-xs text-muted-foreground">
                {config.webhookUrl && (
                  <p className="truncate">Webhook: {config.webhookUrl}</p>
                )}
                <p>{t("admin.chatProviders.messageInterval")}: {config.messageInterval}ms</p>
                <p>{t("admin.chatProviders.maxRetryCount")}: {config.maxRetryCount}</p>
              </div>
              <div className="mt-4 flex items-center justify-between">
                <span className="text-xs text-muted-foreground">{config.platform}</span>
                <Switch
                  checked={config.isEnabled}
                  onCheckedChange={() => handleToggleEnabled(config)}
                />
              </div>
            </Card>
          ))}
        </div>
      )}

      <Dialog open={showDialog} onOpenChange={setShowDialog}>
        <DialogContent className="max-w-2xl">
          <DialogHeader>
            <DialogTitle>
              {editingConfig ? t("admin.chatProviders.edit") : t("admin.chatProviders.create")}
            </DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <div>
                <label className="text-sm font-medium">{t("admin.chatProviders.platform")} *</label>
                <Select
                  value={formData.platform}
                  onValueChange={(value) => {
                    setFormData((prev) => ({ ...prev, platform: value }));
                    setFieldValues({});
                  }}
                  disabled={Boolean(editingConfig)}
                >
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {PLATFORM_OPTIONS.map((option) => (
                      <SelectItem key={option.value} value={option.value}>
                        {t(option.labelKey)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div>
                <label className="text-sm font-medium">{t("admin.chatProviders.displayName")} *</label>
                <Input
                  value={formData.displayName}
                  onChange={(event) => setFormData((prev) => ({ ...prev, displayName: event.target.value }))}
                />
              </div>
            </div>
            <div className="grid gap-4 md:grid-cols-2">
              <div>
                <label className="text-sm font-medium">{t("admin.chatProviders.webhookUrl")}</label>
                <Input
                  value={formData.webhookUrl}
                  onChange={(event) => setFormData((prev) => ({ ...prev, webhookUrl: event.target.value }))}
                />
              </div>
              <div>
                <label className="text-sm font-medium">{t("admin.chatProviders.messageInterval")}</label>
                <Input
                  type="number"
                  value={formData.messageInterval}
                  onChange={(event) =>
                    setFormData((prev) => ({ ...prev, messageInterval: Number(event.target.value) }))
                  }
                />
              </div>
            </div>
            <div className="grid gap-4 md:grid-cols-2">
              <div>
                <label className="text-sm font-medium">{t("admin.chatProviders.maxRetryCount")}</label>
                <Input
                  type="number"
                  value={formData.maxRetryCount}
                  onChange={(event) =>
                    setFormData((prev) => ({ ...prev, maxRetryCount: Number(event.target.value) }))
                  }
                />
              </div>
              <div className="flex items-center justify-between rounded-md border px-3 py-2">
                <label className="text-sm font-medium">{t("admin.chatProviders.enable")}</label>
                <Switch
                  checked={formData.isEnabled}
                  onCheckedChange={(checked) => setFormData((prev) => ({ ...prev, isEnabled: checked }))}
                />
              </div>
            </div>
            <div className="border-t pt-4">
              <h3 className="text-sm font-semibold mb-3">{t("admin.chatProviders.platformConfig")}</h3>
              {renderPlatformFields()}
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowDialog(false)}>
              {t("admin.common.cancel")}
            </Button>
            <Button onClick={handleSave} disabled={saving}>
              {saving ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              {editingConfig ? t("admin.common.save") : t("admin.common.create")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <AlertDialog open={!!deletePlatform} onOpenChange={() => setDeletePlatform(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t("admin.common.confirmDelete")}</AlertDialogTitle>
            <AlertDialogDescription>
              {t("admin.chatProviders.deleteWarning")}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t("admin.common.cancel")}</AlertDialogCancel>
            <AlertDialogAction onClick={handleDelete} className="bg-red-600 hover:bg-red-700">
              {t("admin.common.delete")}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
