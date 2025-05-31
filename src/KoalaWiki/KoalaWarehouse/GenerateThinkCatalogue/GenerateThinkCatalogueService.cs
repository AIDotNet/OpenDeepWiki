using System.Text;
using System.Text.RegularExpressions;
using KoalaWiki.Entities;
using KoalaWiki.Prompts;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Newtonsoft.Json;

namespace KoalaWiki.KoalaWarehouse.GenerateThinkCatalogue;

public class GenerateThinkCatalogueService
{
    public static async Task<(DocumentResultCatalogue catalogue, Exception? exception)> GenerateThinkCatalogue(string path,
        string catalogue, string gitRepository,
        Warehouse warehouse, WarehouseClassify.ClassifyType? classify)
    {
        string prompt = string.Empty;
        if (classify.HasValue)
        {
            prompt = await PromptContext.Warehouse(nameof(PromptConstant.Warehouse.Overview) + classify,
                new KernelArguments()
                {
                    ["code_files"] = catalogue,
                    ["git_repository_url"] = gitRepository.Replace(".git", ""),
                    ["repository_name"] = warehouse.Name
                });
        }
        else
        {
            prompt = await PromptContext.Warehouse(nameof(PromptConstant.Warehouse.Overview),
                new KernelArguments()
                {
                    ["code_files"] = catalogue,
                    ["git_repository_url"] = gitRepository.Replace(".git", ""),
                    ["repository_name"] = warehouse.Name
                });
        }

        DocumentResultCatalogue? result = null;

        var retryCount = 0;
        const int maxRetries = 5;
        Exception? exception = null;

        while (retryCount < maxRetries)
        {
            try
            {
                var analysisModel = KernelFactory.GetKernel(OpenAIOptions.Endpoint,
                    OpenAIOptions.ChatApiKey, path, OpenAIOptions.AnalysisModel, false);

                var chat = analysisModel.Services.GetService<IChatCompletionService>();

                StringBuilder str = new StringBuilder();
                var history = new ChatHistory();
                history.AddUserMessage(prompt);

                await foreach (var item in chat.GetStreamingChatMessageContentsAsync(history,
                                   new OpenAIPromptExecutionSettings()
                                   {
                                       ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                                       Temperature = 0.3,
                                       // 这里使用分析模型的最大token
                                       MaxTokens = DocumentsService.GetMaxTokens(OpenAIOptions.AnalysisModel)
                                   }, analysisModel))
                {
                    // 将推理内容输出
                    str.Append(item);
                }

                result =
                    await GenerateCatalogue(str.ToString(), path, gitRepository, catalogue, warehouse,classify);

                break;
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("处理仓库；{path} ,处理标题：{name} 失败:{ex}", path, warehouse.Name, ex.ToString());
                exception = ex;
                retryCount++;
                if (retryCount >= maxRetries)
                {
                    Console.WriteLine($"处理 {warehouse.Name} 失败，已重试 {retryCount} 次，错误：{ex.Message}");
                }
                else
                {
                    // 等待一段时间后重试
                    await Task.Delay(5000 * retryCount);
                }
            }
        }

        return (result, exception);
    }


    public static async Task<DocumentResultCatalogue> GenerateCatalogue(string think,
        string path, string gitRepository, string catalogue,
        Warehouse warehouse, WarehouseClassify.ClassifyType? classify)
    {
        
        string prompt = string.Empty;
        if (classify.HasValue)
        {
            prompt = await PromptContext.Warehouse(nameof(PromptConstant.Warehouse.Overview) + classify,
                new KernelArguments()
                {
                    ["code_files"] = catalogue,
                    ["think"] = think,
                    ["git_repository_url"] = gitRepository.Replace(".git", ""),
                    ["repository_name"] = warehouse.Name
                });
        }
        else
        {
            prompt = await PromptContext.Warehouse(nameof(PromptConstant.Warehouse.Overview),
                new KernelArguments()
                {
                    ["code_files"] = catalogue,
                    ["think"] = think,
                    ["git_repository_url"] = gitRepository.Replace(".git", ""),
                    ["repository_name"] = warehouse.Name
                });
        }

        
        DocumentResultCatalogue? result = null;

        var retryCount = 0;
        const int maxRetries = 5;
        Exception? exception = null;

        while (retryCount < maxRetries)
        {
            try
            {
                var analysisModel = KernelFactory.GetKernel(OpenAIOptions.Endpoint,
                    OpenAIOptions.ChatApiKey, path, OpenAIOptions.AnalysisModel, false);

                var chat = analysisModel.Services.GetService<IChatCompletionService>();

                StringBuilder str = new StringBuilder();
                var history = new ChatHistory();
                history.AddUserMessage(prompt);

                await foreach (var item in chat.GetStreamingChatMessageContentsAsync(history,
                                   new OpenAIPromptExecutionSettings()
                                   {
                                       ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                                       Temperature = 0.5,
                                       MaxTokens = DocumentsService.GetMaxTokens(OpenAIOptions.AnalysisModel)
                                   }, analysisModel))
                {
                    str.Append(item);
                }

                // 可能需要先处理一下documentation_structure 有些模型不支持json
                var regex = new Regex(@"<documentation_structure>(.*?)</documentation_structure>",
                    RegexOptions.Singleline);
                var match = regex.Match(str.ToString());

                if (match.Success)
                {
                    // 提取到的内容
                    var extractedContent = match.Groups[1].Value;
                    str.Clear();
                    str.Append(extractedContent);
                }

                // 尝试使用```json
                var jsonRegex = new Regex(@"```json(.*?)```", RegexOptions.Singleline);
                var jsonMatch = jsonRegex.Match(str.ToString());
                if (jsonMatch.Success)
                {
                    // 提取到的内容
                    var extractedContent = jsonMatch.Groups[1].Value;
                    str.Clear();
                    str.Append(extractedContent);
                }

                try
                {
                    result = JsonConvert.DeserializeObject<DocumentResultCatalogue>(str.ToString().Trim());
                }
                catch (Exception ex)
                {
                    Log.Logger.Error("反序列化失败: {ex}, 原始字符串: {str}", ex.ToString(), str.ToString().Trim());

                    // 开始尝试重试
                    Log.Logger.Information("处理仓库；{path} ,处理标题：{name} 失败:反序列化失败，开始尝试重试", path, warehouse.Name);
                    throw;
                }

                break;
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("处理仓库；{path} ,处理标题：{name} 失败:{ex}", path, warehouse.Name, ex.ToString());
                exception = ex;
                retryCount++;
                if (retryCount >= maxRetries)
                {
                    Console.WriteLine($"处理 {warehouse.Name} 失败，已重试 {retryCount} 次，错误：{ex.Message}");
                }
                else
                {
                    // 等待一段时间后重试
                    await Task.Delay(5000 * retryCount);
                }
            }
        }

        if (result == null)
        {
            throw new Exception($"处理仓库；{path} ,处理标题：{warehouse.Name} 失败:没有结果，推理内容：{think}");
        }

        return result;
    }
}