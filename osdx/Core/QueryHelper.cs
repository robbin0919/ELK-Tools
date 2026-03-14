using System.Text.Json;

namespace osdx.Core;

public static class QueryHelper
{
    /// <summary>
    /// 智慧解析日期字串，支援日期或日期時間格式
    /// </summary>
    /// <param name="input">使用者輸入</param>
    /// <param name="isEndDate">是否為結束日期 (如果是，且僅輸入日期，則補上 23:59:59)</param>
    /// <returns>格式化後的 ISO 8601 字串</returns>
    public static string ParseSmartDate(string input, bool isEndDate)
    {
        if (DateTime.TryParse(input, out var dt))
        {
            // 檢查輸入是否包含時間部分 (偵測是否存在冒號或 T)
            bool hasTime = input.Contains(":") || input.Contains("T");

            if (!hasTime && isEndDate)
            {
                // 如果是結束日期且沒有時間，則設為該日的最後一秒
                dt = dt.Date.AddDays(1).AddSeconds(-1);
            }

            var dto = new DateTimeOffset(dt, TimeSpan.FromHours(8));
            return dto.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");
        }

        throw new FormatException($"無法解析日期格式: {input}");
    }

    public static string ReplaceTimestampRange(string queryJson, string gteValue, string lteValue)
    {
        try
        {
            var queryObj = JsonSerializer.Deserialize<JsonElement>(queryJson);
            var modifiedObj = ReplaceTimestampInElement(queryObj, gteValue, lteValue);
            return JsonSerializer.Serialize(modifiedObj);
        }
        catch
        {
            return queryJson;
        }
    }

    private static JsonElement ReplaceTimestampInElement(JsonElement element, string gteValue, string lteValue)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var dict = new Dictionary<string, object>();

            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == "range" && property.Value.ValueKind == JsonValueKind.Object)
                {
                    var rangeObj = property.Value;
                    // 找出 range 下第一個含有 gte 或 lte 的欄位（支援 @timestamp / timestamp 等任意欄位名稱）
                    string? matchedField = null;
                    foreach (var fieldProp in rangeObj.EnumerateObject())
                    {
                        if (fieldProp.Value.ValueKind == JsonValueKind.Object &&
                            (fieldProp.Value.TryGetProperty("gte", out _) || fieldProp.Value.TryGetProperty("lte", out _)))
                        {
                            matchedField = fieldProp.Name;
                            break;
                        }
                    }

                    if (matchedField != null && rangeObj.TryGetProperty(matchedField, out var timestampObj))
                    {
                        var newTimestamp = new Dictionary<string, object>();

                        foreach (var tsProp in timestampObj.EnumerateObject())
                        {
                            if (tsProp.Name == "gte")
                                newTimestamp["gte"] = gteValue;
                            else if (tsProp.Name == "lte")
                                newTimestamp["lte"] = lteValue;
                            else
                                newTimestamp[tsProp.Name] = JsonSerializer.Deserialize<object>(tsProp.Value.GetRawText())!;
                        }

                        dict["range"] = new Dictionary<string, object> { { matchedField, newTimestamp } };
                        continue;
                    }
                }

                dict[property.Name] = JsonSerializer.Deserialize<object>(
                    ReplaceTimestampInElement(property.Value, gteValue, lteValue).GetRawText())!;
            }

            return JsonSerializer.SerializeToElement(dict);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var list = new List<object>();
            foreach (var item in element.EnumerateArray())
            {
                list.Add(JsonSerializer.Deserialize<object>(
                    ReplaceTimestampInElement(item, gteValue, lteValue).GetRawText())!);
            }
            return JsonSerializer.SerializeToElement(list);
        }

        return element;
    }
}
