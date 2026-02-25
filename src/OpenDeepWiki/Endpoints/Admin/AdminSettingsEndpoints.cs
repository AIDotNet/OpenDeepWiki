using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using OpenDeepWiki.Models.Admin;
using OpenDeepWiki.Services.Admin;

namespace OpenDeepWiki.Endpoints.Admin;

/// <summary>
/// Admin settings endpoints
/// </summary>
public static class AdminSettingsEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static RouteGroupBuilder MapAdminSettingsEndpoints(this RouteGroupBuilder group)
    {
        var settingsGroup = group.MapGroup("/settings")
            .WithTags("管理端-设置");

        // 获取设置列表
        settingsGroup.MapGet("/", async (
            [FromQuery] string? category,
            [FromServices] IAdminSettingsService settingsService) =>
        {
            var settings = await settingsService.GetSettingsAsync(category);
            return Results.Ok(new { success = true, data = settings });
        })
        .WithName("AdminGetSettings")
        .WithSummary("获取设置列表");

        // 获取单个设置
        settingsGroup.MapGet("/{key}", async (
            string key,
            [FromServices] IAdminSettingsService settingsService) =>
        {
            var setting = await settingsService.GetSettingByKeyAsync(key);
            if (setting == null)
                return Results.NotFound(new { success = false, message = "设置不存在" });
            return Results.Ok(new { success = true, data = setting });
        })
        .WithName("AdminGetSettingByKey")
        .WithSummary("获取单个设置");

        // 更新设置
        settingsGroup.MapPut("/", async (
            [FromBody] List<UpdateSettingRequest> requests,
            [FromServices] IAdminSettingsService settingsService,
            [FromServices] IDynamicConfigManager configManager) =>
        {
            await settingsService.UpdateSettingsAsync(requests);

            // 刷新配置以应用新的设置
            await configManager.RefreshWikiGeneratorOptionsAsync();

            return Results.Ok(new { success = true, message = "设置更新成功" });
        })
        .WithName("AdminUpdateSettings")
        .WithSummary("更新设置");

        // List available models from a provider endpoint
        settingsGroup.MapPost("/list-provider-models", async (
            [FromBody] ListProviderModelsRequest request,
            [FromServices] IHttpClientFactory httpClientFactory) =>
        {
            if (string.IsNullOrWhiteSpace(request.Endpoint) || string.IsNullOrWhiteSpace(request.ApiKey))
            {
                return Results.BadRequest(new { success = false, message = "Endpoint and apiKey are required." });
            }

            try
            {
                using var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(15);

                var baseUrl = request.Endpoint.TrimEnd('/');
                string requestUrl;

                if (string.Equals(request.RequestType, "Anthropic", StringComparison.OrdinalIgnoreCase))
                {
                    requestUrl = $"{baseUrl}/v1/models";
                    client.DefaultRequestHeaders.Add("x-api-key", request.ApiKey);
                    client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                }
                else
                {
                    // OpenAI and OpenAI-compatible providers (OpenAIResponses, etc.)
                    requestUrl = $"{baseUrl}/models";
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", request.ApiKey);
                }

                var response = await client.GetAsync(requestUrl);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    return Results.BadRequest(new
                    {
                        success = false,
                        message = $"Provider returned HTTP {(int)response.StatusCode}: {errorBody}"
                    });
                }

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                var models = new List<ProviderModelInfo>();

                if (doc.RootElement.TryGetProperty("data", out var dataArray) &&
                    dataArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in dataArray.EnumerateArray())
                    {
                        var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                        if (string.IsNullOrEmpty(id))
                            continue;

                        // Strip "models/" prefix (Gemini returns IDs like "models/gemini-2.5-flash")
                        if (id.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                            id = id["models/".Length..];

                        // Anthropic provides display_name; OpenAI-compatible typically does not
                        string? displayName = null;
                        if (item.TryGetProperty("display_name", out var displayNameProp) &&
                            displayNameProp.ValueKind == JsonValueKind.String)
                        {
                            displayName = displayNameProp.GetString();
                        }

                        models.Add(new ProviderModelInfo(id, displayName ?? id));
                    }
                }

                return Results.Ok(new { success = true, data = new { models } });
            }
            catch (TaskCanceledException)
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = "Request to provider timed out after 15 seconds."
                });
            }
            catch (HttpRequestException ex)
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = $"Failed to connect to provider: {ex.Message}"
                });
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = $"Failed to parse provider response: {ex.Message}"
                });
            }
        })
        .WithName("AdminListProviderModels")
        .WithSummary("List available models from a provider endpoint");

        return group;
    }

    /// <summary>
    /// Request body for listing provider models.
    /// </summary>
    internal record ListProviderModelsRequest(
        string Endpoint,
        string ApiKey,
        string RequestType
    );

    /// <summary>
    /// Model info returned from a provider.
    /// </summary>
    internal record ProviderModelInfo(string Id, string DisplayName);
}
