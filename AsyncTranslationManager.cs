using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;
using HttpClient = System.Net.Http.HttpClient;

public partial class AsyncTranslationManager : Node
{
    public static AsyncTranslationManager Instance { get; private set; }
    private  ConcurrentDictionary<string, string> _persistentCache = new ConcurrentDictionary<string, string>(); //持久化缓存，整个翻译流程中共享，避免重复翻译同一文本

    private enum TranslationService //服务器枚举
    {
        Microsoft,
        Baidu,
        Tengxun
    }

    
    private readonly Dictionary<TranslationService, SemaphoreSlim> _serviceSemaphores = new() //控制并发数量，确保不会超过API限制，实现多路并发提升速度
    {
        {TranslationService.Microsoft, new SemaphoreSlim(10) }, //微软并发10
        {TranslationService.Baidu, new SemaphoreSlim(10) }, //百度并发10
        {TranslationService.Tengxun, new SemaphoreSlim(5) } //腾讯并发5
    };

    // 复用 HttpClient 避免端口耗尽
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) }; //设置合理的超时时间

    public override void _Ready()
    {
        Instance = this;

        //加载
        if (SaveManager.Instance.saveData._persistentCache != null)
        {
            foreach (var kvp in SaveManager.Instance.saveData._persistentCache)
            {
                _persistentCache[kvp.Key] = kvp.Value;
            }
        }
    }

    //batchSize是批量大小，onProgress 是一个回调函数，接受已完成的任务数量和总任务数量，用于更新进度显示
    public async Task ProcessTasksParallel(List<InlineTranslation.TranslationTask> tasks, int batchSize, Action<int, int> onProgress)
    {
        if (tasks == null || tasks.Count == 0) return;

        int totalTasks = tasks.Count; //总任务数量
        int currentCompleted = 0; //已完成的任务数量

        // ---去重并筛选出真正需要翻译的---
        var groupedByText = tasks.GroupBy(t => t.OriginalText).ToList(); //按原文分组，键: 原文文本，值: 具有相同原文的任务列表
        var tasksToTranslate = new List<IGrouping<string, InlineTranslation.TranslationTask>>(); //需要调用API的任务列表,按原文分组,元素:任务(同一原文,在不同位置)

        foreach (var textGroup in groupedByText)
        {
            // 检查持久化缓存中是否已经有了
            if (_persistentCache.TryGetValue(textGroup.Key, out string cachedTranslation))
            {
                // 如果缓存中有，直接应用结果
                foreach (var task in textGroup)
                {
                    string restored = RestoreTags(cachedTranslation, task.TagMap);
                    task.WriteBack?.Invoke(restored);
                }
                Interlocked.Add(ref currentCompleted, textGroup.Count());//Interlocked用于在多线程环境下安全地修改变量
                Callable.From(() => onProgress?.Invoke(currentCompleted, totalTasks)).CallDeferred();
            }
            else
            {
                // 缓存没命中，加入翻译清单
                tasksToTranslate.Add(textGroup);
            }
        }

        if (tasksToTranslate.Count == 0)
        {
            GD.Print("[Cache] 所有文本已在缓存中，部分翻译完成！");
            return;
        }

        GD.Print($"[Optimize] 总任务: {totalTasks} | 缓存命中: {currentCompleted} | 需调用API: {tasksToTranslate.Count}");

        // ---开始处理 API 翻译逻辑---

        // 将任务切分为批次
        var batches = new List<List<IGrouping<string, InlineTranslation.TranslationTask>>>();//任务列表
        for (int i = 0; i < tasksToTranslate.Count; i += batchSize)
        {
            //GetRange(i, count)从索引i取出 count 个元素
            batches.Add(tasksToTranslate.GetRange(i, Math.Min(batchSize, tasksToTranslate.Count - i))); //每个批次包含batchSize个唯一文本的翻译任务
        }

        GD.Print($"[Batch] 开始批量翻译，总任务: {totalTasks}, 总批次: {batches.Count}");

        //并行执行批次任务
        var batchTasks = batches.Select(async taskGroup => //每个批次是一个包含多个任务的列表,每个任务对应一个唯一文本
        {
            int maxRetries = 3; // 最大重试次数
            bool isSuccess = false;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                //在循环内为每个批次动态选择服务
                // 如果用户开启了多个翻译源，这里会利用多路并发，极大提升总体翻译速度
                var (activeServiceNullable, semaphore) = SelectBestService(); //选择最闲的翻译源
                if (activeServiceNullable == null)
                {
                    ErrorWindow.ShowError("错误：未找到可用的翻译服务！");
                    break;
                }

                TranslationService activeService = activeServiceNullable.Value;

                await semaphore.WaitAsync(); //获取对应服务的令牌, 如果没有可用令牌则等待，直到有其他批次释放令牌,确保了不会超过每个服务的并发限制
                try
                {
                    // 仅提取这一组中的唯一文本模板进行翻译
                    List<string> uniqueTextsInBatch = taskGroup.Select(g => g.Key).ToList(); //这一批次中唯一的文本列表，发送给翻译API进行批量翻译
                    List<string> translatedResults = await ExecuteBatchTranslation(activeServiceNullable.Value, uniqueTextsInBatch);

                    if (translatedResults != null && translatedResults.Count == taskGroup.Count)
                    {
                        // 翻译成功，写回数据
                        for (int j = 0; j < taskGroup.Count; j++)
                        {
                            string translatedTemplate = translatedResults[j]; //这一批次中第 j 个唯一文本的翻译结果
                            var uniqueTextGroupsBatch = taskGroup[j]; // 这一组里可能有多个任务
                            string originalTemplate = taskGroup[j].Key;

                            _persistentCache[originalTemplate] = translatedTemplate;// 写入持久化内存缓存

                            foreach (var task in uniqueTextGroupsBatch)
                            {
                                // 使用每个任务特有的 TagMap 还原标签（如还原成[烧伤]或[流血]）
                                string restored = RestoreTags(translatedTemplate, task.TagMap);
                                task.WriteBack?.Invoke(restored); //写回翻译结果到原文件，多个任务写同一模板但不同位置时不会冲突
                            }
                        }
                        isSuccess = true;
                        break; // 成功则跳出重试循环
                    }
                    else
                    {
                        GD.PrintErr($"[Batch] 批次翻译返回数量不匹配或失败。服务: {activeService}, 尝试: {attempt}/{maxRetries}");
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[Batch] 批次处理异常: {ex.Message} 服务: {activeService}, 尝试: {attempt}/{maxRetries}");
                }
                finally
                {
                    semaphore.Release(); // 释放令牌，让其他批次可以使用该服务
                }

                // 如果失败且还有重试次数，执行指数退避延迟 (1秒, 2秒, 4秒...) 避免被服务器拒绝访问
                if (!isSuccess && attempt < maxRetries)
                {
                    await Task.Delay(1000 * (int)Math.Pow(2, attempt - 1));// 指数退避重试
                }
            }

            // 无论重试后是成功还是失败,增加进度
            int batchOriginalCount = taskGroup.Sum(g => g.Count());
            Interlocked.Add(ref currentCompleted, batchOriginalCount);
            Callable.From(() => onProgress?.Invoke(currentCompleted, totalTasks)).CallDeferred();
        });

        //等待所有批次任务（包括重试）彻底执行完毕
        await Task.WhenAll(batchTasks);
        SaveCacheToDisk();
        GD.Print("[Batch] 所有批量翻译任务流程执行完毕。");
    }

    //API 选择逻辑
    private (TranslationService? service, SemaphoreSlim semaphore) SelectBestService()
    {
        var save = SaveManager.Instance.saveData;
        var candidates = new List<TranslationService>(); //翻译源列表

        if (save.isMicrosofttranslationEnable) candidates.Add(TranslationService.Microsoft);
        if (save.isBaidutranslationEnable) candidates.Add(TranslationService.Baidu);
        if (save.isTengxuntranslationEnable) candidates.Add(TranslationService.Tengxun);

        if (candidates.Count == 0) return (null, null);

        TranslationService bestService = candidates[0];
        int maxAvailableSlots = -1;

        foreach (var service in candidates)
        {
            int currentFree = _serviceSemaphores[service].CurrentCount;
            if (currentFree > maxAvailableSlots)
            {
                maxAvailableSlots = currentFree;
                bestService = service;
            }
        }

        return (bestService, _serviceSemaphores[bestService]);
    }


    //发给翻译API的批量翻译执行逻辑，根据不同服务调用不同的批量翻译方法
    private async Task<List<string>> ExecuteBatchTranslation(TranslationService service, List<string> texts)
    {
        return service switch
        {
            TranslationService.Microsoft => await TranslateMicrosoftBatch(texts),
            TranslationService.Baidu => await TranslateBaiduBatch(texts),
            TranslationService.Tengxun => await TranslateTengxunBatch(texts),
            _ => null
        };
    }

    // ---微软批量 (原生支持 JSON 对象数组) ---
    private async Task<List<string>> TranslateMicrosoftBatch(List<string> texts)
    {
        string fromLang = "ja";
        string toLang = "zh-Hans";
        string region = "eastasia"; // 固定区域，与你的翻译资源所在区域一致

        string url = $"{SaveManager.Instance.saveData.MicrosoftranslationUrl}translate?api-version=3.0&from={fromLang}&to={toLang}";

        // 微软格式: [{"Text":"..."}, {"Text":"..."}]
        var body = texts.Select(t => new { Text = t }).ToList();
        string jsonBody = JsonSerializer.Serialize(body);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        request.Headers.Add("Ocp-Apim-Subscription-Key", SaveManager.Instance.saveData.MicrosofttranslationKey);
        request.Headers.Add("Ocp-Apim-Subscription-Region", region);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("translations")[0].GetProperty("text").GetString())
            .ToList();
    }

    // ---百度批量 (使用 \n 拼接，带严格行数校验) ---
    private async Task<List<string>> TranslateBaiduBatch(List<string> texts)
    {
        string apiUrl = "https://fanyi-api.baidu.com/api/trans/vip/translate";
        string appId = SaveManager.Instance.saveData.BaidutranslationUrl; //百度翻译应用ID
        string appKey = SaveManager.Instance.saveData.BaidutranslationKey; //百度翻译密钥

        string q = string.Join("\n", texts);
        string salt = new Random().Next(100000, 999999).ToString(); //随机盐值
        string signSource = appId + q + salt + appKey;
        string sign = ComputeMD5HexLower(signSource);

        var content = new FormUrlEncodedContent(new[] {
            new KeyValuePair<string, string>("q", q),
            new KeyValuePair<string, string>("from", "jp"),
            new KeyValuePair<string, string>("to", "zh"),
            new KeyValuePair<string, string>("appid", appId),
            new KeyValuePair<string, string>("salt", salt),
            new KeyValuePair<string, string>("sign", sign)
        });

        var response = await _httpClient.PostAsync(apiUrl, content);
        var json = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("trans_result", out var results))
        {
            var list = results.EnumerateArray().Select(x => x.GetProperty("dst").GetString()).ToList();
            // 校验：百度会把两行合成一行，如果行数不对，舍弃，否则会写错位
            return list.Count == texts.Count ? list : null;
        }
        return null;
    }

    private async Task<List<string>> TranslateTengxunBatch(List<string> texts)
    {
        string tengxunFromLang = "ja";
        string tengxunToLang = "zh";

        string secretId = SaveManager.Instance.saveData.TengxuntranslationUrl;
        string secretKey = SaveManager.Instance.saveData.TengxuntranslationKey;
        string endpoint = "tmt.tencentcloudapi.com";
        string region = "ap-guangzhou";

        var requestParams = new Godot.Collections.Dictionary<string, Variant> //构建请求参数
        {
            { "SourceTextList", texts.ToArray() },
            { "Source", tengxunFromLang },
            { "Target", tengxunToLang },
            { "ProjectId", 0 }
        };
        string service = "tmt"; //腾讯云翻译服务标识
        string host = "tmt.tencentcloudapi.com"; // 就近接入域名
        string action = "TextTranslateBatch"; //批量接口名称
        string version = "2018-03-21"; //接口版本
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(); //当前时间戳

        string authorization = GenerateTC3Signature(
        secretId, secretKey, timestamp, service, host, region, action, version, requestParams);

        string url = $"https://{host}/";

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        string jsonBody = Json.Stringify(requestParams);
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        request.Headers.Add("Host", host);
        request.Headers.Add("X-TC-Action", action);
        request.Headers.Add("X-TC-Version", version);
        request.Headers.Add("X-TC-Timestamp", timestamp);
        request.Headers.Add("X-TC-Region", region);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);

        var response = await _httpClient.SendAsync(request);
        var responseJson = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.GetProperty("Response").TryGetProperty("TargetTextList", out var results))
        {
            return results.EnumerateArray().Select(x => x.GetString()).ToList();
        }
        return null;
    }


    //---单条翻译逻辑---
    // 微软翻译
    private async Task<string> TranslateMicrosoft(string text)
    {
        string fromLang = "ja";
        string toLang = "zh-Hans";
        string region = "eastasia"; // 固定区域，与你的翻译资源所在区域一致

        string url = $"{SaveManager.Instance.saveData.MicrosoftranslationUrl}translate?api-version=3.0&from={fromLang}&to={toLang}";

        //构建JSON请求体: [{"Text": "要翻译的文本"}]
        string escapedText = EscapeJson(text); //转义特殊字符
        string jsonBody = $"[{{\"Text\": \"{escapedText}\"}}]"; //微软要求数组格式
        byte[] bodyBytes = System.Text.Encoding.UTF8.GetBytes(jsonBody); //将JSON变成UTF-8字节数组

        //准备请求头
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        request.Headers.Add("Ocp-Apim-Subscription-Key", SaveManager.Instance.saveData.MicrosofttranslationKey);
        request.Headers.Add("Ocp-Apim-Subscription-Region", region);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        string resBody = await response.Content.ReadAsStringAsync();
        var jsonArray = Json.ParseString(resBody).AsGodotArray();
        return jsonArray[0].AsGodotDictionary()["translations"].AsGodotArray()[0].AsGodotDictionary()["text"].ToString();
    }

    //百度翻译
    private async Task<string> TranslateBaidu(string text)
    {
        string baiduFromLang = "jp";
        string baiduToLang = "zh";

        string apiUrl = "https://fanyi-api.baidu.com/api/trans/vip/translate";
        string appId = SaveManager.Instance.saveData.BaidutranslationUrl; //百度翻译应用ID
        string appKey = SaveManager.Instance.saveData.BaidutranslationKey; //百度翻译密钥

        string salt = new Random().Next(100000, 999999).ToString(); //随机盐值
        string signSource = appId + text + salt + appKey;
        string sign = ComputeMD5HexLower(signSource);

        string form = $"q={Uri.EscapeDataString(text)}&from={Uri.EscapeDataString(baiduFromLang)}&to={Uri.EscapeDataString(baiduToLang)}&appid={Uri.EscapeDataString(appId)}&salt={Uri.EscapeDataString(salt)}&sign={Uri.EscapeDataString(sign)}";
        byte[] bodyBytes = Encoding.UTF8.GetBytes(form);

        string[] headers =
        {
            "Content-Type: application/x-www-form-urlencoded",
            $"Content-Length: {bodyBytes.Length}"
        };

        var response = await _httpClient.GetAsync($"{apiUrl}?{form}");
        if (!response.IsSuccessStatusCode) return null;

        string resBody = await response.Content.ReadAsStringAsync();
        var resDict = Json.ParseString(resBody).AsGodotDictionary();
        if (resDict.ContainsKey("trans_result"))
        {
            return resDict["trans_result"].AsGodotArray()[0].AsGodotDictionary()["dst"].ToString();
        }
        return null;
    }

    //腾讯翻译
    private async Task<string> TranslateTengxun(string text)
    {
        string tengxunFromLang = "ja";
        string tengxunToLang = "zh";

        string secretId = SaveManager.Instance.saveData.TengxuntranslationUrl;
        string secretKey = SaveManager.Instance.saveData.TengxuntranslationKey;
        string endpoint = "tmt.tencentcloudapi.com";
        string region = "ap-guangzhou";

        var requestParams = new Godot.Collections.Dictionary<string, Variant> //构建请求参数
        {
            { "SourceText", text },
            { "Source", tengxunFromLang },
            { "Target", tengxunToLang },
            { "ProjectId", 0 }
        };
        string service = "tmt"; //腾讯云翻译服务标识
        string host = "tmt.tencentcloudapi.com"; // 就近接入域名
        string action = "TextTranslate"; //接口名称
        string version = "2018-03-21"; //接口版本
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(); //当前时间戳

        string authorization = GenerateTC3Signature(
        secretId, secretKey, timestamp, service, host, region, action, version, requestParams);

        string url = $"https://{host}/";

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        string jsonBody = Json.Stringify(requestParams);
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        request.Headers.Add("Host", host);
        request.Headers.Add("X-TC-Action", action);
        request.Headers.Add("X-TC-Version", version);
        request.Headers.Add("X-TC-Timestamp", timestamp);
        request.Headers.Add("X-TC-Region", region);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);

        var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            string resBody = await response.Content.ReadAsStringAsync();
            var resDict = Json.ParseString(resBody).AsGodotDictionary();
            var responseNode = resDict["Response"].AsGodotDictionary();
            if (responseNode.ContainsKey("TargetText"))
                return responseNode["TargetText"].ToString();
        }
        return null;
    }

    //辅助方法
    private static string ComputeMD5HexLower(string input) //计算MD5哈希值并返回小写十六进制字符串
    {
        using (var md5 = MD5.Create())
        {
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    private string GenerateTC3Signature(
    string secretId, string secretKey, string timestamp,
    string service, string host, string region, string action, string version,
    Godot.Collections.Dictionary<string, Variant> requestParams)
    {
        //拼接规范请求字符串（CanonicalRequest）
        string httpRequestMethod = "POST";
        string canonicalUri = "/";
        string canonicalQueryString = "";
        string canonicalHeaders =
            "content-type:application/json; charset=utf-8\n" +  
            "host:" + host + "\n";
        string signedHeaders = "content-type;host";

        //请求体哈希（SHA256）
        string payload = Json.Stringify(requestParams);
        string hashedRequestPayload = SHA256Hex(payload);
        string canonicalRequest =
            httpRequestMethod + "\n" +
            canonicalUri + "\n" +
            canonicalQueryString + "\n" +
            canonicalHeaders + "\n" +
            signedHeaders + "\n" +
            hashedRequestPayload;

        //拼接待签名字符串（StringToSign）
        string algorithm = "TC3-HMAC-SHA256";
        string requestTimestamp = timestamp;
        string date = DateTimeOffset.FromUnixTimeSeconds(long.Parse(timestamp)).UtcDateTime.ToString("yyyy-MM-dd");
        string credentialScope = $"{date}/{service}/tc3_request";
        string hashedCanonicalRequest = SHA256Hex(canonicalRequest);
        string stringToSign =
            algorithm + "\n" +
            requestTimestamp + "\n" +
            credentialScope + "\n" +
            hashedCanonicalRequest;

        //计算签名
        byte[] secretDate = HmacSHA256(Encoding.UTF8.GetBytes("TC3" + secretKey), date);
        byte[] secretService = HmacSHA256(secretDate, service);
        byte[] secretSigning = HmacSHA256(secretService, "tc3_request");
        byte[] signatureBytes = HmacSHA256(secretSigning, stringToSign);
        string signature = BitConverter.ToString(signatureBytes).Replace("-", "").ToLower();

        //构造 Authorization 头
        string authorization =
            $"{algorithm} Credential={secretId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";

        return authorization;
    }

    // 辅助函数：SHA256 十六进制
    private string SHA256Hex(string data)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
    }

    // 辅助函数：HMAC-SHA256
    private byte[] HmacSHA256(byte[] key, string data)
    {
        using (HMACSHA256 hmac = new HMACSHA256(key))
        {
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        }
    }

    private string RestoreTags(string translatedText, Dictionary<string, string> tagMap)
    {
        foreach (var kvp in tagMap)
        {
            string id = kvp.Key; // 这里获取到的是纯数字 "0", "1"
            string originalTag = kvp.Value; // 这里是 "[Laceration]" 或 "<color=red>"

            //完美匹配：[TAG0], 【TAG0】, [tag 0 ], { { tag_0} }, TAG0 等所有变体
            string pattern = $@"([\[【\{{<（]*\s*[Tt][Aa][Gg][_\s]*{id}\s*[\]】\}}>）]*)"; 
            translatedText = Regex.Replace(translatedText, pattern, originalTag);
        }
        return translatedText;
    }

    private void SaveCacheToDisk()
    {
        // 将 ConcurrentDictionary 转回普通的 Dictionary 给 SaveManager
        SaveManager.Instance.saveData._persistentCache = new Dictionary<string, string>(_persistentCache);
        SaveManager.Instance.SaveDataToFile();
    }

    private string EscapeJson(string text)
    {
        return text.Replace("\\", "\\\\")
                   .Replace("\"", "\\\"")
                   .Replace("\n", "\\n")
                   .Replace("\r", "\\r")
                   .Replace("\t", "\\t");
    }
}