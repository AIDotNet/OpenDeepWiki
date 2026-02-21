"use client";

import React, { useEffect, useState, useCallback } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
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
  getModelConfigs,
  createModelConfig,
  updateModelConfig,
  deleteModelConfig,
  listProviderModels,
  ModelConfig,
  ProviderModel,
} from "@/lib/admin-api";
import {
  Loader2,
  Trash2,
  Edit,
  RefreshCw,
  Plus,
  Bot,
  CheckCircle,
  XCircle,
  Star,
  Download,
} from "lucide-react";
import { toast } from "sonner";
import { useTranslations } from "@/hooks/use-translations";

const PROVIDER_PRESETS: Record<string, { endpoint: string; requestType: string }> = {
  OpenAI: { endpoint: "https://api.openai.com/v1", requestType: "OpenAI" },
  Anthropic: { endpoint: "https://api.anthropic.com", requestType: "Anthropic" },
  Google: { endpoint: "https://generativelanguage.googleapis.com/v1beta/openai/", requestType: "OpenAI" },
  AzureOpenAI: { endpoint: "", requestType: "AzureOpenAI" },
  Custom: { endpoint: "", requestType: "OpenAI" },
};

export default function AdminModelsPage() {
  const [configs, setConfigs] = useState<ModelConfig[]>([]);
  const [loading, setLoading] = useState(true);
  const [showDialog, setShowDialog] = useState(false);
  const [editingConfig, setEditingConfig] = useState<ModelConfig | null>(null);
  const [deleteId, setDeleteId] = useState<string | null>(null);
  const [formData, setFormData] = useState({
    name: "",
    provider: "OpenAI",
    modelId: "",
    endpoint: "",
    apiKey: "",
    isDefault: false,
    isActive: true,
    description: "",
  });
  const [providerModels, setProviderModels] = useState<ProviderModel[]>([]);
  const [loadingModels, setLoadingModels] = useState(false);
  const t = useTranslations();

  const providers = [
    { value: "OpenAI", label: "OpenAI" },
    { value: "Anthropic", label: "Anthropic" },
    { value: "AzureOpenAI", label: "Azure OpenAI" },
    { value: "Google", label: "Google" },
    { value: "Custom", label: t('admin.models.custom') },
  ];

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getModelConfigs();
      setConfigs(result);
    } catch (error) {
      console.error("Failed to fetch Model configs:", error);
      toast.error(t('admin.toast.fetchModelFailed'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const handleProviderChange = (provider: string) => {
    const preset = PROVIDER_PRESETS[provider];
    setFormData({
      ...formData,
      provider,
      endpoint: preset?.endpoint || "",
      modelId: "",
    });
    setProviderModels([]);
  };

  const handleLoadModels = async () => {
    const preset = PROVIDER_PRESETS[formData.provider];
    const endpoint = formData.endpoint || preset?.endpoint;
    if (!endpoint || !formData.apiKey) return;

    setLoadingModels(true);
    try {
      const requestType = preset?.requestType || "OpenAI";
      const models = await listProviderModels(endpoint, formData.apiKey, requestType);
      setProviderModels(models);

      // Keep current modelId if it exists in new list
      const modelIds = new Set(models.map((m) => m.id));
      if (formData.modelId && !modelIds.has(formData.modelId)) {
        setFormData((prev) => ({ ...prev, modelId: "" }));
      }

      toast.success(t('admin.models.modelsLoaded', { count: String(models.length) }));
    } catch (err) {
      const message = err instanceof Error ? err.message : t('admin.models.loadModelsFailed');
      toast.error(message);
    } finally {
      setLoadingModels(false);
    }
  };

  const handleModelSelect = (modelId: string) => {
    const model = providerModels.find((m) => m.id === modelId);
    setFormData((prev) => ({
      ...prev,
      modelId,
      // Auto-fill display name if empty
      name: prev.name || (model?.displayName ?? modelId),
    }));
  };

  const openCreateDialog = () => {
    setEditingConfig(null);
    setFormData({
      name: "",
      provider: "OpenAI",
      modelId: "",
      endpoint: PROVIDER_PRESETS["OpenAI"].endpoint,
      apiKey: "",
      isDefault: false,
      isActive: true,
      description: "",
    });
    setProviderModels([]);
    setShowDialog(true);
  };

  const openEditDialog = (config: ModelConfig) => {
    setEditingConfig(config);
    setFormData({
      name: config.name,
      provider: config.provider,
      modelId: config.modelId,
      endpoint: config.endpoint || "",
      apiKey: config.apiKey || "",
      isDefault: config.isDefault,
      isActive: config.isActive,
      description: config.description || "",
    });
    // Create synthetic model entry so current model appears in dropdown
    if (config.modelId) {
      setProviderModels([{ id: config.modelId, displayName: config.name || config.modelId }]);
    } else {
      setProviderModels([]);
    }
    setShowDialog(true);
  };

  const handleSave = async () => {
    if (!formData.name.trim() || !formData.modelId.trim()) {
      toast.error(t('admin.toast.fillRequired'));
      return;
    }
    try {
      if (editingConfig) {
        await updateModelConfig(editingConfig.id, formData);
        toast.success(t('admin.toast.updateSuccess'));
      } else {
        await createModelConfig(formData);
        toast.success(t('admin.toast.createSuccess'));
      }
      setShowDialog(false);
      fetchData();
    } catch (error) {
      toast.error(editingConfig ? t('admin.toast.updateFailed') : t('admin.toast.createFailed'));
    }
  };

  const handleDelete = async () => {
    if (!deleteId) return;
    try {
      await deleteModelConfig(deleteId);
      toast.success(t('admin.toast.deleteSuccess'));
      setDeleteId(null);
      fetchData();
    } catch (error) {
      toast.error(t('admin.toast.deleteFailed'));
    }
  };

  const handleToggleActive = async (config: ModelConfig) => {
    try {
      await updateModelConfig(config.id, { isActive: !config.isActive });
      toast.success(config.isActive ? t('admin.models.disabled') : t('admin.models.enabled'));
      fetchData();
    } catch (error) {
      toast.error(t('admin.toast.operationFailed'));
    }
  };

  const handleSetDefault = async (config: ModelConfig) => {
    if (config.isDefault) return;
    try {
      await updateModelConfig(config.id, { isDefault: true });
      toast.success(t('admin.toast.setDefaultSuccess'));
      fetchData();
    } catch (error) {
      toast.error(t('admin.toast.operationFailed'));
    }
  };

  const canLoadModels = formData.endpoint && formData.apiKey;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">{t('admin.models.title')}</h1>
        <div className="flex gap-2">
          <Button variant="outline" onClick={fetchData}>
            <RefreshCw className="mr-2 h-4 w-4" />
            {t('admin.common.refresh')}
          </Button>
          <Button onClick={openCreateDialog}>
            <Plus className="mr-2 h-4 w-4" />
            {t('admin.models.createModel')}
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
            <Bot className="mx-auto h-12 w-12 text-muted-foreground" />
            <p className="mt-4 text-muted-foreground">{t('admin.models.noModels')}</p>
            <Button className="mt-4" onClick={openCreateDialog}>
              <Plus className="mr-2 h-4 w-4" />
              {t('admin.models.addFirst')}
            </Button>
          </div>
        </Card>
      ) : (
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
          {configs.map((config) => (
            <Card key={config.id} className={`p-6 ${config.isDefault ? "ring-2 ring-primary" : ""}`}>
              <div className="flex items-start justify-between">
                <div className="flex items-center gap-3">
                  <div className={`rounded-full p-2 ${config.isActive ? "bg-blue-100 dark:bg-blue-900" : "bg-gray-100 dark:bg-gray-800"}`}>
                    <Bot className={`h-5 w-5 ${config.isActive ? "text-blue-600 dark:text-blue-400" : "text-gray-500"}`} />
                  </div>
                  <div>
                    <div className="flex items-center gap-2">
                      <h3 className="font-semibold">{config.name}</h3>
                      {config.isDefault && (
                        <Star className="h-4 w-4 text-yellow-500 fill-yellow-500" />
                      )}
                    </div>
                    <p className="text-xs text-muted-foreground">{config.provider}</p>
                  </div>
                </div>
                <div className="flex gap-1">
                  <Button variant="ghost" size="icon" onClick={() => openEditDialog(config)}>
                    <Edit className="h-4 w-4" />
                  </Button>
                  <Button variant="ghost" size="icon" onClick={() => setDeleteId(config.id)}>
                    <Trash2 className="h-4 w-4 text-red-500" />
                  </Button>
                </div>
              </div>
              <div className="mt-3">
                <p className="text-sm font-mono bg-muted px-2 py-1 rounded inline-block">
                  {config.modelId}
                </p>
              </div>
              {config.description && (
                <p className="mt-2 text-sm text-muted-foreground line-clamp-2">
                  {config.description}
                </p>
              )}
              <div className="mt-4 flex items-center justify-between">
                <div className="flex items-center gap-2">
                  {config.isActive ? (
                    <span className="flex items-center gap-1 text-xs text-green-600">
                      <CheckCircle className="h-3 w-3" /> {t('admin.models.enabled')}
                    </span>
                  ) : (
                    <span className="flex items-center gap-1 text-xs text-gray-400">
                      <XCircle className="h-3 w-3" /> {t('admin.models.disabled')}
                    </span>
                  )}
                </div>
                <div className="flex items-center gap-2">
                  {!config.isDefault && (
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleSetDefault(config)}
                    >
                      {t('admin.models.setDefault')}
                    </Button>
                  )}
                  <Switch
                    checked={config.isActive}
                    onCheckedChange={() => handleToggleActive(config)}
                  />
                </div>
              </div>
            </Card>
          ))}
        </div>
      )}

      {/* Create/Edit Dialog */}
      <Dialog open={showDialog} onOpenChange={setShowDialog}>
        <DialogContent className="max-w-lg">
          <DialogHeader>
            <DialogTitle>{editingConfig ? t('admin.models.editModel') : t('admin.models.createModel')}</DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            <div>
              <label className="text-sm font-medium">{t('admin.models.provider')} *</label>
              <Select
                value={formData.provider}
                onValueChange={handleProviderChange}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {providers.map((p) => (
                    <SelectItem key={p.value} value={p.value}>
                      {p.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div>
              <label className="text-sm font-medium">{t('admin.models.apiKey')}</label>
              <Input
                type="password"
                value={formData.apiKey}
                onChange={(e) => setFormData({ ...formData, apiKey: e.target.value })}
                placeholder={t('admin.mcps.optional')}
              />
            </div>
            <div>
              <label className="text-sm font-medium">{t('admin.models.apiEndpoint')}</label>
              <div className="flex gap-2">
                <Input
                  value={formData.endpoint}
                  onChange={(e) => setFormData({ ...formData, endpoint: e.target.value })}
                  placeholder={t('admin.models.endpointPlaceholder')}
                  className="flex-1"
                />
                <Button
                  variant="outline"
                  onClick={handleLoadModels}
                  disabled={!canLoadModels || loadingModels}
                >
                  {loadingModels ? (
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  ) : (
                    <Download className="mr-2 h-4 w-4" />
                  )}
                  {t('admin.models.loadModels')}
                </Button>
              </div>
            </div>
            <div>
              <label className="text-sm font-medium">{t('admin.models.modelId')} *</label>
              {providerModels.length > 0 ? (
                <div className="space-y-2">
                  <Select
                    value={formData.modelId}
                    onValueChange={handleModelSelect}
                  >
                    <SelectTrigger>
                      <SelectValue placeholder={t('admin.models.selectModel')} />
                    </SelectTrigger>
                    <SelectContent>
                      {providerModels.map((m) => (
                        <SelectItem key={m.id} value={m.id}>
                          {m.displayName}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <p className="text-xs text-muted-foreground">{t('admin.models.orTypeManually')}</p>
                  <Input
                    value={formData.modelId}
                    onChange={(e) => setFormData({ ...formData, modelId: e.target.value })}
                    placeholder={t('admin.models.modelIdPlaceholder')}
                  />
                </div>
              ) : (
                <Input
                  value={formData.modelId}
                  onChange={(e) => setFormData({ ...formData, modelId: e.target.value })}
                  placeholder={t('admin.models.modelIdPlaceholder')}
                />
              )}
            </div>
            <div>
              <label className="text-sm font-medium">{t('admin.models.displayName')} *</label>
              <Input
                value={formData.name}
                onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                placeholder={t('admin.models.namePlaceholder')}
              />
            </div>
            <div>
              <label className="text-sm font-medium">{t('admin.models.description')}</label>
              <Textarea
                value={formData.description}
                onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                placeholder={t('admin.models.modelDesc')}
                rows={2}
              />
            </div>
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Switch
                  checked={formData.isDefault}
                  onCheckedChange={(checked) => setFormData({ ...formData, isDefault: checked })}
                />
                <label className="text-sm">{t('admin.models.setAsDefault')}</label>
              </div>
              <div className="flex items-center gap-2">
                <Switch
                  checked={formData.isActive}
                  onCheckedChange={(checked) => setFormData({ ...formData, isActive: checked })}
                />
                <label className="text-sm">{t('admin.models.enable')}</label>
              </div>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowDialog(false)}>{t('admin.common.cancel')}</Button>
            <Button onClick={handleSave}>{editingConfig ? t('admin.common.save') : t('admin.common.create')}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Delete confirmation dialog */}
      <AlertDialog open={!!deleteId} onOpenChange={() => setDeleteId(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('admin.common.confirmDelete')}</AlertDialogTitle>
            <AlertDialogDescription>
              {t('admin.models.deleteWarning')}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('admin.common.cancel')}</AlertDialogCancel>
            <AlertDialogAction onClick={handleDelete} className="bg-red-600 hover:bg-red-700">
              {t('admin.common.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
