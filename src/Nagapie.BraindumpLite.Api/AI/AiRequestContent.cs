using System.Text.Json;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Api;

internal static class AiRequestContent
{
    public static JsonContent Create(string model, ProcessDumpRequest request)
    {
        var schema = JsonSerializer.Deserialize<JsonElement>("""
            {
              "type": "object",
              "properties": {
                "items": {
                  "type": "array",
                  "minItems": 1,
                  "maxItems": 30,
                  "items": {
                    "type": "object",
                    "properties": {
                      "text": {
                        "type": "string"
                      },
                      "suggestedCategoryId": {
                        "type": [
                          "string",
                          "null"
                        ]
                      },
                      "planningHorizon": {
                        "type": "string",
                        "enum": [
                          "today",
                          "tomorrow",
                          "later"
                        ]
                      }
                    },
                    "required": [
                      "text",
                      "suggestedCategoryId",
                      "planningHorizon"
                    ],
                    "additionalProperties": false
                  }
                }
              },
              "required": [
                "items"
              ],
              "additionalProperties": false
            }
            """);
        var input = JsonSerializer.Serialize(new
        {
            categories = request.AvailableCategories,
            text = request.Text
        });

        return JsonContent.Create(new
        {
            model,
            store = false,
            messages = new[]
            {
                new { role = "system", content = BrainDumpPrompt.Text },
                new { role = "user", content = input }
            },
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "brain_dump",
                    strict = true,
                    schema
                }
            }
        });
    }
}
