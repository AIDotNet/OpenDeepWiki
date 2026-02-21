"use client";

import React, { useEffect, useState, useCallback, useMemo } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  getSettings,
  updateSettings,
  listProviderModels,
  SystemSetting,
  ProviderModel,
} from "@/lib/admin-api";
import {
  Loader2,
  RefreshCw,
  Save,
  Settings,
  Shield,
  Bot,
  Globe,
  Zap,
  Info,
} from "lucide-react";
import { toast } from "sonner";
import { useTranslations } from "@/hooks/use-translations";

type SettingGroupId = "default" | "aiContent" | "aiCatalog" | "aiTranslation" | "aiRuntime" | "aiOther";

interface SettingGroup {
  id: SettingGroupId;
  settings: SystemSetting[];
}

const PROVIDER_PRESETS: Record<string, { label: string; endpoint: string; requestType: string }> = {
  gemini: {
    label: "Google Gemini",
    endpoint: "https://generativelanguage.googleapis.com/v1beta/openai/",
    requestType: "OpenAI",
  },
  openai: {
    label: "OpenAI",
    endpoint: "https://api.openai.com/v1",
    requestType: "OpenAI",
  },
  anthropic: {
    label: "Anthropic",
    endpoint: "https://api.anthropic.com",
    requestType: "Anthropic",
  },
};

export default function AdminSettingsPage() {
  const [settings, setSettings] = useState<SystemSetting[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [activeCategory, setActiveCategory] = useState("general");
  const [editedValues, setEditedValues] = useState<Record<string, string>>({});
  const t = useTranslations();

  // Quick Setup state
  const [selectedProvider, setSelectedProvider] = useState<string>("");
  const [quickSetupApiKey, setQuickSetupApiKey] = useState("");
  const [providerModels, setProviderModels] = useState<ProviderModel[]>([]);
  const [loadingModels, setLoadingModels] = useState(false);
  const [selectedModels, setSelectedModels] = useState({
    catalog: "",
    content: "",
    translation: "",
  });

  const categoryIcons: Record<string, React.ReactNode> = {
    general: <Globe className="h-4 w-4" />,
    ai: <Bot className="h-4 w-4" />,
    security: <Shield className="h-4 w-4" />,
  };

  const categoryLabels: Record<string, string> = {
    general: t('admin.settings.general'),
    ai: t('admin.settings.ai'),
    security: t('admin.settings.security'),
  };

  const requestTypeOptions = ["OpenAI", "OpenAIResponses", "Anthropic"];

  const resolveAiGroupId = useCallback((key: string): SettingGroupId => {
    const upperKey = key.toUpperCase();
    if (upperKey.includes("TRANSLATION")) return "aiTranslation";
    if (upperKey.includes("CONTENT_") || upperKey === "WIKI_MAX_OUTPUT_TOKENS") return "aiContent";
    if (
      upperKey.includes("CATALOG") ||
      upperKey.includes("DIRECTORY_TREE") ||
      upperKey === "WIKI_LANGUAGES" ||
      upperKey === "WIKI_PROMPTS_DIRECTORY" ||
      upperKey === "WIKI_README_MAX_LENGTH"
    ) {
      return "aiCatalog";
    }
    if (upperKey.includes("PARALLEL") || upperKey.includes("RETRY") || upperKey.includes("TIMEOUT")) {
      return "aiRuntime";
    }
    return "aiOther";
  }, []);

  const getGroupMeta = useCallback(
    (category: string, groupId: SettingGroupId) => {
      if (category !== "ai") {
        return {
          title: t("admin.settings.groupTitles.default"),
          description: t("admin.settings.groupDescriptions.default"),
        };
      }

      switch (groupId) {
        case "aiContent":
          return {
            title: t("admin.settings.groupTitles.aiContent"),
            description: t("admin.settings.groupDescriptions.aiContent"),
          };
        case "aiCatalog":
          return {
            title: t("admin.settings.groupTitles.aiCatalog"),
            description: t("admin.settings.groupDescriptions.aiCatalog"),
          };
        case "aiTranslation":
          return {
            title: t("admin.settings.groupTitles.aiTranslation"),
            description: t("admin.settings.groupDescriptions.aiTranslation"),
          };
        case "aiRuntime":
          return {
            title: t("admin.settings.groupTitles.aiRuntime"),
            description: t("admin.settings.groupDescriptions.aiRuntime"),
          };
        case "aiOther":
          return {
            title: t("admin.settings.groupTitles.aiOther"),
            description: t("admin.settings.groupDescriptions.aiOther"),
          };
        default:
          return {
            title: t("admin.settings.groupTitles.default"),
            description: t("admin.settings.groupDescriptions.default"),
          };
      }
    },
    [t]
  );

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getSettings();
      setSettings(result);
      const values: Record<string, string> = {};
      result.forEach((s) => {
        values[s.key] = s.value || "";
      });
      setEditedValues(values);
    } catch (error) {
      console.error("Failed to fetch settings:", error);
      toast.error(t('admin.toast.fetchSettingsFailed'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const formatSettingLabel = useCallback((key: string) => {
    return key
      .toLowerCase()
      .split("_")
      .map((chunk) => chunk.charAt(0).toUpperCase() + chunk.slice(1))
      .join(" ");
  }, []);

  const handleFieldChange = useCallback((key: string, value: string) => {
    setEditedValues((prev) => ({
      ...prev,
      [key]: value,
    }));
  }, []);

  const handleSave = async () => {
    setSaving(true);
    try {
      const changedSettings = settings
        .filter((s) => editedValues[s.key] !== (s.value || ""))
        .map((s) => ({ key: s.key, value: editedValues[s.key] }));

      if (changedSettings.length === 0) {
        toast.info(t('admin.settings.noChanges'));
        setSaving(false);
        return;
      }

      await updateSettings(changedSettings);
      toast.success(t('admin.toast.saveSuccess'));
      fetchData();
    } catch {
      toast.error(t('admin.toast.saveFailed'));
    } finally {
      setSaving(false);
    }
  };

  // Quick Setup: Load models from provider
  const handleLoadModels = async () => {
    const preset = PROVIDER_PRESETS[selectedProvider];
    if (!preset || !quickSetupApiKey) return;

    setLoadingModels(true);
    setProviderModels([]);
    setSelectedModels({ catalog: "", content: "", translation: "" });

    try {
      const models = await listProviderModels(preset.endpoint, quickSetupApiKey, preset.requestType);
      setProviderModels(models);
      toast.success(t('admin.settings.quickSetup.modelsLoaded', { count: models.length }));
    } catch (err) {
      const message = err instanceof Error ? err.message : t('admin.settings.quickSetup.loadFailed');
      toast.error(message);
    } finally {
      setLoadingModels(false);
    }
  };

  // Quick Setup: Apply selected configuration to all settings
  const handleApplyQuickSetup = () => {
    const preset = PROVIDER_PRESETS[selectedProvider];
    if (!preset) return;

    const settingsMap: Record<string, string> = {
      WIKI_CATALOG_MODEL: selectedModels.catalog,
      WIKI_CATALOG_ENDPOINT: preset.endpoint,
      WIKI_CATALOG_API_KEY: quickSetupApiKey,
      WIKI_CATALOG_REQUEST_TYPE: preset.requestType,
      WIKI_CONTENT_MODEL: selectedModels.content,
      WIKI_CONTENT_ENDPOINT: preset.endpoint,
      WIKI_CONTENT_API_KEY: quickSetupApiKey,
      WIKI_CONTENT_REQUEST_TYPE: preset.requestType,
      WIKI_TRANSLATION_MODEL: selectedModels.translation,
      WIKI_TRANSLATION_ENDPOINT: preset.endpoint,
      WIKI_TRANSLATION_API_KEY: quickSetupApiKey,
      WIKI_TRANSLATION_REQUEST_TYPE: preset.requestType,
    };

    for (const [key, value] of Object.entries(settingsMap)) {
      handleFieldChange(key, value);
    }

    toast.success(t('admin.settings.quickSetup.applied'));
  };

  const canLoadModels = selectedProvider && quickSetupApiKey;
  const canApply = selectedModels.catalog && selectedModels.content && selectedModels.translation;

  const categories = useMemo(() => [...new Set(settings.map((s) => s.category))], [settings]);

  const settingsByCategory = useMemo(() => {
    return categories.reduce<Record<string, SystemSetting[]>>((acc, cat) => {
      acc[cat] = settings.filter((s) => s.category === cat);
      return acc;
    }, {});
  }, [categories, settings]);

  const groupedSettingsByCategory = useMemo(() => {
    return categories.reduce<Record<string, SettingGroup[]>>((acc, cat) => {
      const categorySettings = settingsByCategory[cat] ?? [];
      if (cat !== "ai") {
        acc[cat] = [{ id: "default", settings: categorySettings }];
        return acc;
      }

      const bucket: Record<SettingGroupId, SystemSetting[]> = {
        aiContent: [],
        aiCatalog: [],
        aiTranslation: [],
        aiRuntime: [],
        aiOther: [],
        default: [],
      };

      categorySettings.forEach((setting) => {
        const groupId = resolveAiGroupId(setting.key);
        bucket[groupId].push(setting);
      });

      const order: SettingGroupId[] = ["aiContent", "aiCatalog", "aiTranslation", "aiRuntime", "aiOther"];
      acc[cat] = order.filter((id) => bucket[id].length > 0).map((id) => ({ id, settings: bucket[id] }));
      return acc;
    }, {});
  }, [categories, settingsByCategory, resolveAiGroupId]);

  const activeSettings = settingsByCategory[activeCategory] ?? [];

  useEffect(() => {
    if (categories.length === 0) return;
    if (!categories.includes(activeCategory)) {
      setActiveCategory(categories[0] ?? "general");
    }
  }, [categories, activeCategory]);

  const hasChanges = settings.some((s) => editedValues[s.key] !== (s.value || ""));
  const pendingChangeCount = settings.reduce((count, setting) => {
    return count + (editedValues[setting.key] !== (setting.value || "") ? 1 : 0);
  }, 0);
  const hasSettings = settings.length > 0;

  if (loading) {
    return (
      <div className="flex h-64 items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">{t('admin.settings.title')}</h1>
        <div className="flex gap-2">
          <Button variant="outline" onClick={fetchData}>
            <RefreshCw className="mr-2 h-4 w-4" />
            {t('admin.common.refresh')}
          </Button>
          <Button onClick={handleSave} disabled={saving || !hasChanges}>
            {saving ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <Save className="mr-2 h-4 w-4" />
            )}
            {t('admin.settings.saveChanges')}
          </Button>
        </div>
      </div>

      {!hasSettings ? (
        <Card className="flex h-64 items-center justify-center">
          <div className="text-center">
            <Settings className="mx-auto h-12 w-12 text-muted-foreground" />
            <p className="mt-4 text-muted-foreground">{t('admin.settings.noSettings')}</p>
          </div>
        </Card>
      ) : (
        <Tabs value={activeCategory} onValueChange={setActiveCategory} className="space-y-6">
          <div className="grid gap-6 lg:grid-cols-[280px,1fr]">
            <div className="space-y-4">
              <Card className="border-primary/20 bg-gradient-to-br from-primary/10 via-background to-background p-5 text-sm">
                <p className="text-xs font-semibold uppercase tracking-wide text-primary">
                  {t('admin.settings.title')}
                </p>
                <div className="mt-4 space-y-4">
                  <div className="flex items-center gap-4">
                    <div className="rounded-full bg-primary/15 p-3 text-primary">
                      {categoryIcons[activeCategory] || <Settings className="h-6 w-6" />}
                    </div>
                    <div>
                      <p className="text-xs uppercase text-muted-foreground">{t('admin.settings.activeCategory')}</p>
                      <p className="text-lg font-semibold">
                        {categoryLabels[activeCategory] || activeCategory}
                      </p>
                      <p className="text-xs text-muted-foreground">
                        {t('admin.settings.categorySummary', {
                          settings: activeSettings.length,
                          pending: pendingChangeCount,
                        })}
                      </p>
                    </div>
                  </div>
                  <div className="grid grid-cols-2 gap-3">
                    <div className="rounded-xl border border-primary/20 bg-background/40 p-3">
                      <p className="text-[11px] uppercase tracking-wide text-muted-foreground">
                        {t('admin.settings.statCategories')}
                      </p>
                      <p className="text-xl font-semibold">{categories.length}</p>
                    </div>
                    <div className="rounded-xl border border-primary/20 bg-background/40 p-3">
                      <p className="text-[11px] uppercase tracking-wide text-muted-foreground">
                        {t('admin.settings.statFields')}
                      </p>
                      <p className="text-xl font-semibold">{settings.length}</p>
                    </div>
                  </div>
                </div>
              </Card>

              <Card className="p-4">
                <div className="mb-3 flex items-center justify-between text-xs uppercase tracking-wide text-muted-foreground">
                  <span>{t('admin.settings.categoryNavigation')}</span>
                  <span>{t('admin.settings.categoriesLabel')}</span>
                </div>
                <div className="sticky top-24 space-y-3">
                  <TabsList className="flex flex-col gap-3 bg-transparent p-0">
                    {categories.map((cat) => (
                      <TabsTrigger
                        key={cat}
                        value={cat}
                        className="flex w-full flex-col gap-1 rounded-xl border border-border/60 px-4 py-3 text-left transition hover:border-primary/50 hover:bg-primary/5 data-[state=active]:border-primary data-[state=active]:bg-primary/10"
                      >
                        <div className="flex items-center justify-between text-sm font-medium">
                          <div className="flex items-center gap-2">
                            {categoryIcons[cat] || <Settings className="h-4 w-4" />}
                            {categoryLabels[cat] || cat}
                          </div>
                          <Badge variant="secondary" className="text-[11px]">
                            {(settingsByCategory[cat]?.length ?? 0)}
                          </Badge>
                        </div>
                        <span className="text-xs text-muted-foreground">
                          {t('admin.settings.manageCategory')}
                        </span>
                      </TabsTrigger>
                    ))}
                  </TabsList>
                </div>
              </Card>
            </div>

            <div className="space-y-6">
              {categories.map((cat) => {
                const categorySettings = settingsByCategory[cat] ?? [];
                const groupedSettings = groupedSettingsByCategory[cat] ?? [{ id: "default", settings: categorySettings }];
                return (
                  <TabsContent key={cat} value={cat} className="mt-0 space-y-6">
                    {/* Quick Setup card - only shown on AI tab */}
                    {cat === "ai" && (
                      <Card className="border-primary/30 bg-gradient-to-r from-primary/5 to-background p-6">
                        <div className="flex items-center gap-2 mb-4">
                          <Zap className="h-5 w-5 text-primary" />
                          <h3 className="text-lg font-semibold">{t('admin.settings.quickSetup.title')}</h3>
                        </div>
                        <p className="text-sm text-muted-foreground mb-5">
                          {t('admin.settings.quickSetup.description')}
                        </p>

                        <div className="grid gap-4 md:grid-cols-2">
                          {/* Provider selection */}
                          <div className="space-y-2">
                            <label className="text-sm font-medium">
                              {t('admin.settings.quickSetup.provider')}
                            </label>
                            <Select
                              value={selectedProvider || undefined}
                              onValueChange={(value) => {
                                setSelectedProvider(value);
                                setProviderModels([]);
                                setSelectedModels({ catalog: "", content: "", translation: "" });
                              }}
                            >
                              <SelectTrigger>
                                <SelectValue placeholder={t('admin.settings.quickSetup.selectProvider')} />
                              </SelectTrigger>
                              <SelectContent>
                                {Object.entries(PROVIDER_PRESETS).map(([key, preset]) => (
                                  <SelectItem key={key} value={key}>
                                    {preset.label}
                                  </SelectItem>
                                ))}
                              </SelectContent>
                            </Select>
                          </div>

                          {/* API Key */}
                          <div className="space-y-2">
                            <label className="text-sm font-medium">
                              {t('admin.settings.quickSetup.apiKey')}
                            </label>
                            <div className="flex gap-2">
                              <Input
                                type="password"
                                value={quickSetupApiKey}
                                onChange={(e) => setQuickSetupApiKey(e.target.value)}
                                placeholder={t('admin.settings.quickSetup.apiKeyPlaceholder')}
                              />
                              <Button
                                onClick={handleLoadModels}
                                disabled={!canLoadModels || loadingModels}
                                variant="outline"
                              >
                                {loadingModels ? (
                                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                                ) : null}
                                {t('admin.settings.quickSetup.loadModels')}
                              </Button>
                            </div>
                          </div>
                        </div>

                        {/* Model selection - shown after models are loaded */}
                        {providerModels.length > 0 && (
                          <div className="mt-5 space-y-4">
                            <div className="flex items-center gap-2">
                              <Badge variant="secondary">
                                {t('admin.settings.quickSetup.modelsLoaded', { count: providerModels.length })}
                              </Badge>
                            </div>

                            <div className="grid gap-4 md:grid-cols-3">
                              {/* Catalog Model */}
                              <div className="space-y-2">
                                <label className="text-sm font-medium">
                                  {t('admin.settings.quickSetup.catalogModel')}
                                </label>
                                <Select
                                  value={selectedModels.catalog || undefined}
                                  onValueChange={(value) =>
                                    setSelectedModels((prev) => ({ ...prev, catalog: value }))
                                  }
                                >
                                  <SelectTrigger>
                                    <SelectValue placeholder={t('admin.settings.quickSetup.selectModel')} />
                                  </SelectTrigger>
                                  <SelectContent>
                                    {providerModels.map((model) => (
                                      <SelectItem key={model.id} value={model.id}>
                                        {model.displayName}
                                      </SelectItem>
                                    ))}
                                  </SelectContent>
                                </Select>
                                <p className="flex items-start gap-1 text-xs text-muted-foreground">
                                  <Info className="h-3 w-3 mt-0.5 shrink-0" />
                                  {t('admin.settings.quickSetup.catalogModelHint')}
                                </p>
                              </div>

                              {/* Content Model */}
                              <div className="space-y-2">
                                <label className="text-sm font-medium">
                                  {t('admin.settings.quickSetup.contentModel')}
                                </label>
                                <Select
                                  value={selectedModels.content || undefined}
                                  onValueChange={(value) =>
                                    setSelectedModels((prev) => ({ ...prev, content: value }))
                                  }
                                >
                                  <SelectTrigger>
                                    <SelectValue placeholder={t('admin.settings.quickSetup.selectModel')} />
                                  </SelectTrigger>
                                  <SelectContent>
                                    {providerModels.map((model) => (
                                      <SelectItem key={model.id} value={model.id}>
                                        {model.displayName}
                                      </SelectItem>
                                    ))}
                                  </SelectContent>
                                </Select>
                                <p className="flex items-start gap-1 text-xs text-muted-foreground">
                                  <Info className="h-3 w-3 mt-0.5 shrink-0" />
                                  {t('admin.settings.quickSetup.contentModelHint')}
                                </p>
                              </div>

                              {/* Translation Model */}
                              <div className="space-y-2">
                                <label className="text-sm font-medium">
                                  {t('admin.settings.quickSetup.translationModel')}
                                </label>
                                <Select
                                  value={selectedModels.translation || undefined}
                                  onValueChange={(value) =>
                                    setSelectedModels((prev) => ({ ...prev, translation: value }))
                                  }
                                >
                                  <SelectTrigger>
                                    <SelectValue placeholder={t('admin.settings.quickSetup.selectModel')} />
                                  </SelectTrigger>
                                  <SelectContent>
                                    {providerModels.map((model) => (
                                      <SelectItem key={model.id} value={model.id}>
                                        {model.displayName}
                                      </SelectItem>
                                    ))}
                                  </SelectContent>
                                </Select>
                                <p className="flex items-start gap-1 text-xs text-muted-foreground">
                                  <Info className="h-3 w-3 mt-0.5 shrink-0" />
                                  {t('admin.settings.quickSetup.translationModelHint')}
                                </p>
                              </div>
                            </div>

                            <Button onClick={handleApplyQuickSetup} disabled={!canApply}>
                              {t('admin.settings.quickSetup.apply')}
                            </Button>
                          </div>
                        )}
                      </Card>
                    )}

                    <Card className="p-6">
                      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border/60 pb-4">
                        <div>
                          <p className="text-xs uppercase tracking-wide text-muted-foreground">
                            {t('admin.settings.categoryHeading')}
                          </p>
                          <h2 className="text-xl font-semibold">
                            {categoryLabels[cat] || cat}
                          </h2>
                        </div>
                        <div className="flex items-center gap-2">
                          <Badge variant="outline" className="text-xs uppercase tracking-wide">
                            {t('admin.settings.settingsCount', { count: categorySettings.length })}
                          </Badge>
                          {pendingChangeCount > 0 && (
                            <Badge className="text-xs">
                              {t('admin.settings.pendingCount', { count: pendingChangeCount })}
                            </Badge>
                          )}
                        </div>
                      </div>

                      <div className="mt-6 space-y-6">
                        {groupedSettings.map((group) => {
                          const groupMeta = getGroupMeta(cat, group.id);
                          return (
                            <section key={`${cat}-${group.id}`} className="space-y-3">
                              <div className="flex flex-wrap items-center justify-between gap-2">
                                <div>
                                  <h3 className="text-sm font-semibold">{groupMeta.title}</h3>
                                  <p className="text-xs text-muted-foreground">{groupMeta.description}</p>
                                </div>
                                <Badge variant="secondary" className="text-[11px] uppercase tracking-wide">
                                  {t('admin.settings.settingsCount', { count: group.settings.length })}
                                </Badge>
                              </div>

                              <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
                                {group.settings.map((setting) => {
                                  const lowerKey = setting.key.toLowerCase();
                                  const isTemplateField = (setting.value?.length || 0) > 100 || lowerKey.includes("template");
                                  const isSensitiveField =
                                    lowerKey.includes("key") ||
                                    lowerKey.includes("secret") ||
                                    lowerKey.includes("password");
                                  const isSelectField = setting.key.endsWith("_REQUEST_TYPE");
                                  const currentValue = editedValues[setting.key] || "";
                                  const hasPendingChange = currentValue !== (setting.value || "");

                                  return (
                                    <div
                                      key={setting.key}
                                      className="flex flex-col gap-3 rounded-2xl border border-border/70 bg-card/50 p-4 shadow-sm backdrop-blur"
                                    >
                                      <div className="flex items-start justify-between gap-3">
                                        <div>
                                          <p className="text-sm font-semibold text-foreground">
                                            {formatSettingLabel(setting.key)}
                                          </p>
                                          <p className="text-[11px] font-mono uppercase tracking-wide text-muted-foreground">
                                            {setting.key}
                                          </p>
                                          {setting.description && (
                                            <p className="mt-1 text-xs text-muted-foreground line-clamp-2">
                                              {setting.description}
                                            </p>
                                          )}
                                        </div>
                                        {hasPendingChange && (
                                          <Badge className="shrink-0 text-[11px]">
                                            {t('admin.settings.pendingChange')}
                                          </Badge>
                                        )}
                                      </div>

                                      {isSelectField ? (
                                        <Select
                                          value={currentValue || undefined}
                                          onValueChange={(value) => handleFieldChange(setting.key, value)}
                                        >
                                          <SelectTrigger>
                                            <SelectValue placeholder={t('admin.settings.selectRequestType')} />
                                          </SelectTrigger>
                                          <SelectContent>
                                            {requestTypeOptions.map((option) => (
                                              <SelectItem key={option} value={option}>
                                                {t(`admin.settings.requestTypeOptions.${option}`)}
                                              </SelectItem>
                                            ))}
                                          </SelectContent>
                                        </Select>
                                      ) : isTemplateField ? (
                                        <Textarea
                                          value={currentValue}
                                          onChange={(e) => handleFieldChange(setting.key, e.target.value)}
                                          rows={4}
                                          className="font-mono text-sm"
                                        />
                                      ) : isSensitiveField ? (
                                        <Input
                                          type="password"
                                          value={currentValue}
                                          onChange={(e) => handleFieldChange(setting.key, e.target.value)}
                                        />
                                      ) : (
                                        <Input
                                          value={currentValue}
                                          onChange={(e) => handleFieldChange(setting.key, e.target.value)}
                                        />
                                      )}
                                    </div>
                                  );
                                })}
                              </div>
                            </section>
                          );
                        })}
                      </div>
                    </Card>
                  </TabsContent>
                );
              })}
            </div>
          </div>
        </Tabs>
      )}

      {hasChanges && (
        <div className="fixed bottom-6 right-6">
          <Button onClick={handleSave} disabled={saving} size="lg" className="shadow-lg">
            {saving ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <Save className="mr-2 h-4 w-4" />
            )}
            {t('admin.settings.saveChanges')}
          </Button>
        </div>
      )}
    </div>
  );
}
