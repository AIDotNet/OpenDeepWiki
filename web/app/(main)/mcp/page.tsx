"use client";

import { useEffect, useState } from "react";
import { AppLayout } from "@/components/app-layout";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  Globe,
  Key,
  Copy,
  Check,
  ExternalLink,
  Loader2,
  Zap,
  Shield,
  Code2,
} from "lucide-react";
import { useTranslations } from "@/hooks/use-translations";
import { api } from "@/lib/api-client";

interface McpProviderPublic {
  id: string;
  name: string;
  description?: string;
  serverUrl: string;
  transportType: string;
  requiresApiKey: boolean;
  apiKeyObtainUrl?: string;
  iconUrl?: string;
  maxRequestsPerDay: number;
  allowedTools?: string;
}

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);

  async function handleCopy() {
    await navigator.clipboard.writeText(text);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  return (
    <button
      onClick={handleCopy}
      className="ml-2 inline-flex items-center text-muted-foreground hover:text-foreground transition-colors"
    >
      {copied ? <Check className="h-3.5 w-3.5 text-green-500" /> : <Copy className="h-3.5 w-3.5" />}
    </button>
  );
}

function CodeBlock({ code, language = "json" }: { code: string; language?: string }) {
  const [copied, setCopied] = useState(false);

  async function handleCopy() {
    await navigator.clipboard.writeText(code);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  return (
    <div className="relative rounded-lg bg-muted/60 border">
      <div className="flex items-center justify-between px-4 py-2 border-b">
        <span className="text-xs text-muted-foreground font-mono">{language}</span>
        <button
          onClick={handleCopy}
          className="flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors"
        >
          {copied ? (
            <><Check className="h-3.5 w-3.5 text-green-500" /> Copied</>
          ) : (
            <><Copy className="h-3.5 w-3.5" /> Copy</>
          )}
        </button>
      </div>
      <pre className="p-4 text-sm overflow-x-auto font-mono leading-relaxed">
        <code>{code}</code>
      </pre>
    </div>
  );
}

export default function McpPage() {
  const t = useTranslations();
  const [providers, setProviders] = useState<McpProviderPublic[]>([]);
  const [loading, setLoading] = useState(true);
  const [origin, setOrigin] = useState("");

  function buildRepositoryScopedServerUrl(serverUrl: string) {
    const template = (serverUrl || "/api/mcp/{owner}/{repo}")
      .replace("{owner}", "<owner>")
      .replace("{repo}", "<repo>");

    if (!origin || /^https?:\/\//.test(template)) {
      return template;
    }

    return `${origin}${template.startsWith("/") ? "" : "/"}${template}`;
  }

  useEffect(() => {
    setOrigin(window.location.origin);

    api
      .get<{ success: boolean; data: McpProviderPublic[] }>("/api/mcp-providers", {
        skipAuth: true,
      })
      .then((res) => {
        if (res.success) setProviders(res.data);
      })
      .catch(() => {})
      .finally(() => setLoading(false));
  }, []);

  return (
    <AppLayout>
      <div className="max-w-5xl mx-auto px-4 py-10 space-y-10">
        {/* Hero */}
        <div className="text-center space-y-4">
          <div className="inline-flex items-center gap-2 rounded-full bg-primary/10 px-4 py-1.5 text-sm font-medium text-primary">
            <Zap className="h-4 w-4" />
            Model Context Protocol
          </div>
          <h1 className="text-4xl font-bold tracking-tight">{t("common.mcp.title")}</h1>
          <p className="text-lg text-muted-foreground max-w-2xl mx-auto">
            {t("common.mcp.description")}
          </p>
        </div>

        {/* Feature cards */}
        <div className="grid gap-4 md:grid-cols-3">
          <Card className="border-dashed">
            <CardHeader className="pb-3">
              <div className="rounded-full bg-blue-100 dark:bg-blue-900 w-10 h-10 flex items-center justify-center mb-2">
                <Code2 className="h-5 w-5 text-blue-600 dark:text-blue-400" />
              </div>
              <CardTitle className="text-base">{t("common.mcp.featureStandardTitle")}</CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-sm text-muted-foreground">{t("common.mcp.featureStandardDesc")}</p>
            </CardContent>
          </Card>
          <Card className="border-dashed">
            <CardHeader className="pb-3">
              <div className="rounded-full bg-green-100 dark:bg-green-900 w-10 h-10 flex items-center justify-center mb-2">
                <Shield className="h-5 w-5 text-green-600 dark:text-green-400" />
              </div>
              <CardTitle className="text-base">{t("common.mcp.featureAuthTitle")}</CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-sm text-muted-foreground">{t("common.mcp.featureAuthDesc")}</p>
            </CardContent>
          </Card>
          <Card className="border-dashed">
            <CardHeader className="pb-3">
              <div className="rounded-full bg-purple-100 dark:bg-purple-900 w-10 h-10 flex items-center justify-center mb-2">
                <Zap className="h-5 w-5 text-purple-600 dark:text-purple-400" />
              </div>
              <CardTitle className="text-base">{t("common.mcp.featureToolsTitle")}</CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-sm text-muted-foreground">{t("common.mcp.featureToolsDesc")}</p>
            </CardContent>
          </Card>
        </div>

        {/* Provider list */}
        <div className="space-y-4">
          <h2 className="text-2xl font-semibold">{t("common.mcp.availableProviders")}</h2>
          {loading ? (
            <div className="flex items-center justify-center py-16">
              <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
            </div>
          ) : providers.length === 0 ? (
            <Card>
              <CardContent className="flex flex-col items-center justify-center py-16 text-center">
                <Globe className="h-12 w-12 text-muted-foreground mb-4" />
                <p className="text-lg font-medium">{t("common.mcp.noProviders")}</p>
                <p className="text-sm text-muted-foreground mt-1">{t("common.mcp.noProvidersHint")}</p>
              </CardContent>
            </Card>
          ) : (
            <div className="space-y-4">
              {providers.map((provider) => {
                const repositoryScopedServerUrl = buildRepositoryScopedServerUrl(provider.serverUrl);

                return (
                <Card key={provider.id}>
                  <CardHeader>
                    <div className="flex items-start justify-between gap-4">
                      <div className="flex items-center gap-3">
                        {provider.iconUrl ? (
                          <img src={provider.iconUrl} alt="" className="h-8 w-8 rounded-lg" />
                        ) : (
                          <div className="h-8 w-8 rounded-lg bg-muted flex items-center justify-center">
                            <Globe className="h-4 w-4 text-muted-foreground" />
                          </div>
                        )}
                        <div>
                          <CardTitle className="text-lg">{provider.name}</CardTitle>
                          {provider.description && (
                            <CardDescription className="mt-0.5">{provider.description}</CardDescription>
                          )}
                        </div>
                      </div>
                      <div className="flex flex-wrap gap-1.5 shrink-0">
                        <Badge variant="secondary" className="text-xs">{provider.transportType}</Badge>
                        {provider.requiresApiKey && (
                          <Badge variant="outline" className="text-xs gap-1">
                            <Key className="h-3 w-3" />
                            {t("common.mcp.requiresApiKey")}
                          </Badge>
                        )}
                        {provider.maxRequestsPerDay > 0 && (
                          <Badge variant="outline" className="text-xs">
                            {provider.maxRequestsPerDay} {t("common.mcp.reqPerDay")}
                          </Badge>
                        )}
                      </div>
                    </div>
                  </CardHeader>
                  <CardContent>
                    <Tabs defaultValue="config">
                      <TabsList className="mb-4">
                        <TabsTrigger value="config">{t("common.mcp.tabConfig")}</TabsTrigger>
                        <TabsTrigger value="claude">{t("common.mcp.tabClaude")}</TabsTrigger>
                        <TabsTrigger value="cursor">{t("common.mcp.tabCursor")}</TabsTrigger>
                        {provider.allowedTools && (
                          <TabsTrigger value="tools">{t("common.mcp.tabTools")}</TabsTrigger>
                        )}
                      </TabsList>

                      <TabsContent value="config" className="space-y-3">
                        <div className="flex items-center gap-2 text-sm">
                          <span className="text-muted-foreground w-28 shrink-0">{t("common.mcp.serverUrl")}:</span>
                          <code className="font-mono text-xs bg-muted px-2 py-1 rounded flex-1 break-all">
                            {repositoryScopedServerUrl}
                          </code>
                          <CopyButton text={repositoryScopedServerUrl} />
                        </div>
                        <div className="flex items-center gap-2 text-sm">
                          <span className="text-muted-foreground w-28 shrink-0">{t("common.mcp.transport")}:</span>
                          <code className="font-mono text-xs bg-muted px-2 py-1 rounded">{provider.transportType}</code>
                        </div>
                        {provider.requiresApiKey && (
                          <div className="rounded-lg border border-amber-200 bg-amber-50 dark:border-amber-800 dark:bg-amber-950/30 p-3 text-sm">
                            <p className="font-medium text-amber-800 dark:text-amber-300 flex items-center gap-1.5">
                              <Key className="h-3.5 w-3.5" />
                              {t("common.mcp.apiKeyRequired")}
                            </p>
                            <p className="text-amber-700 dark:text-amber-400 mt-1 text-xs">
                              {t("common.mcp.apiKeyHint")}
                            </p>
                            {provider.apiKeyObtainUrl && (
                              <a
                                href={provider.apiKeyObtainUrl}
                                target="_blank"
                                rel="noopener noreferrer"
                                className="inline-flex items-center gap-1 mt-2 text-xs text-amber-700 dark:text-amber-400 hover:underline font-medium"
                              >
                                <ExternalLink className="h-3 w-3" />
                                {t("common.mcp.getApiKey")}
                              </a>
                            )}
                          </div>
                        )}
                      </TabsContent>

                      <TabsContent value="claude">
                        <CodeBlock
                          language="json (claude_desktop_config.json)"
                          code={JSON.stringify(
                            {
                              mcpServers: {
                                [provider.name.toLowerCase().replace(/\s+/g, "-")]: {
                                  command: "npx",
                                  args: [
                                    "-y",
                                    "@modelcontextprotocol/client-http",
                                    repositoryScopedServerUrl,
                                  ],
                                  ...(provider.requiresApiKey
                                    ? {
                                        env: {
                                          MCP_API_KEY: "<your-api-key>",
                                        },
                                      }
                                    : {}),
                                },
                              },
                            },
                            null,
                            2
                          )}
                        />
                        <p className="text-xs text-muted-foreground mt-2">
                          {t("common.mcp.claudeHint")}
                        </p>
                      </TabsContent>

                      <TabsContent value="cursor">
                        <CodeBlock
                          language="json (.cursor/mcp.json)"
                          code={JSON.stringify(
                            {
                              mcpServers: {
                                [provider.name.toLowerCase().replace(/\s+/g, "-")]: {
                                  url: repositoryScopedServerUrl,
                                  transport: provider.transportType,
                                  ...(provider.requiresApiKey
                                    ? {
                                        headers: {
                                          Authorization: "Bearer <your-api-key>",
                                        },
                                      }
                                    : {}),
                                },
                              },
                            },
                            null,
                            2
                          )}
                        />
                        <p className="text-xs text-muted-foreground mt-2">
                          {t("common.mcp.cursorHint")}
                        </p>
                      </TabsContent>

                      {provider.allowedTools && (
                        <TabsContent value="tools">
                          <div className="flex flex-wrap gap-2">
                            {(() => {
                              try {
                                const tools: string[] = JSON.parse(provider.allowedTools);
                                return tools.map((tool) => (
                                  <Badge key={tool} variant="secondary" className="font-mono text-xs">
                                    {tool}
                                  </Badge>
                                ));
                              } catch {
                                return (
                                  <code className="text-xs text-muted-foreground">{provider.allowedTools}</code>
                                );
                              }
                            })()}
                          </div>
                        </TabsContent>
                      )}
                    </Tabs>
                  </CardContent>
                </Card>
                );
              })}
            </div>
          )}
        </div>

        {/* General usage guide */}
        <div className="space-y-4">
          <h2 className="text-2xl font-semibold">{t("common.mcp.generalGuide")}</h2>
          <Card>
            <CardContent className="pt-6 space-y-4">
              <p className="text-sm text-muted-foreground">{t("common.mcp.generalGuideDesc")}</p>
              <CodeBlock
                language="http"
                code={`GET /api/mcp-providers HTTP/1.1
Host: <your-server>

# Response
{
  "success": true,
  "data": [
    {
      "id": "...",
      "name": "My MCP Provider",
      "serverUrl": "/api/mcp/{owner}/{repo}",
      "transportType": "streamable_http",
      "requiresApiKey": true
    }
  ]
}`}
              />
            </CardContent>
          </Card>
        </div>
      </div>
    </AppLayout>
  );
}
