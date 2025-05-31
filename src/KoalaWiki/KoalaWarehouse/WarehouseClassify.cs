using System.Text.RegularExpressions;
using KoalaWiki.Prompts;
using Microsoft.SemanticKernel;

namespace KoalaWiki.KoalaWarehouse;

public class WarehouseClassify
{
    /// <summary>
    /// 根据仓库信息分析得出仓库分类
    /// </summary>
    public static async Task<ClassifyType?> ClassifyAsync(Kernel kernel, string catalog, string readme)
    {
        var prompt = await PromptContext.Warehouse(nameof(PromptConstant.Warehouse.RepositoryClassification),
            new KernelArguments()
            {
                ["DIRECTORY_STRUCTURE"] = catalog,
                ["README"] = readme
            });

        var result = await kernel.InvokePromptAsync(prompt);


        // 提取分类结果正则表达式<classify>(.*?)</classify>
        var regex = new Regex(@"<classify>(.*?)</classify>", RegexOptions.Singleline);
        var match = regex.Match(result.ToString());
        if (match.Success)
        {
            // 提取到的内容
            var extractedContent = match.Groups[1].Value.Replace("classifyName:", "").Trim();

            // 将提取的内容转换为枚举类型
            if (Enum.TryParse<ClassifyType>(extractedContent, true, out var classifyType))
            {
                return classifyType;
            }
            else
            {
                return null;
            }
        }
        else
        {
            return null;
        }
    }

    public enum ClassifyType
    {
        /// <summary>
        /// 应用系统
        /// </summary>
        Applications,

        /// <summary>
        /// Projects providing development foundation and architecture
        /// </summary>
        Frameworks,

        /// <summary>
        /// Libraries providing reusable code components
        /// </summary>
        Libraries,

        /// <summary>
        /// Tools and utilities for development
        /// </summary>
        DevelopmentTools,

        /// <summary>
        /// Projects related to data processing and analysis
        /// </summary>
        CLITools,

        /// <summary>
        /// Projects related to DevOps and CI/CD
        /// </summary>
        DevOpsConfiguration,

        /// <summary>
        /// Projects related to testing and quality assurance
        /// </summary>
        Documentation
    }
}