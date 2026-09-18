using System.Text.Json;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Api;

public static class AiOutputValidator
{
    public static ProcessDumpResponse Validate(string json, ProcessDumpRequest request)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.GetProperty("items").EnumerateArray().Select(item =>
            {
                var text = item.GetProperty("text").GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(text) || text.Length > 500)
                {
                    throw new AiFailure(ErrorCodes.AiInvalidOutput);
                }

                Guid? id = Guid.TryParse(item.GetProperty("suggestedCategoryId").GetString(), out var parsed) &&
            request.AvailableCategories.Any(c => c.Id == parsed) ? parsed : null;
                var horizon = item.GetProperty("planningHorizon").GetString();
                return new SplitItemDto(text, id, horizon is "today" or "tomorrow" ? horizon : "later");
            }).ToList();
            if (items.Count is < 1 or > 30)
            {
                throw new AiFailure(ErrorCodes.AiInvalidOutput);
            }

            return new(request.SourceDumpId, items, BrainDumpPrompt.Version);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            throw new AiFailure(ErrorCodes.AiInvalidOutput);
        }
    }
}
