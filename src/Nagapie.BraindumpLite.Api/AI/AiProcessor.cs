using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Api;

public sealed class AiProcessor(HttpClient http, IOptions<AiProviderOptions> options) : IAiBrainDumpProcessor
{
    public async Task<ProcessDumpResponse> ProcessAsync(ProcessDumpRequest request, CancellationToken ct)
    {
        var key = options.Value.ApiKey;
        var model = options.Value.Model;
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(model))
        {
            throw new AiFailure(ErrorCodes.AiNotConfigured);
        }

        var baseUrl = options.Value.BaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
        {
            throw new AiFailure(ErrorCodes.AiNotConfigured);
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, uri.AbsoluteUri.TrimEnd('/') + "/chat/completions");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        message.Content = AiRequestContent.Create(model, request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await http.SendAsync(message, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new AiFailure(await ClassifyFailure(response, timeout.Token));
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var choice = json.RootElement.GetProperty("choices")[0];
            if (choice.GetProperty("finish_reason").GetString() != "stop")
            {
                throw new AiFailure(ErrorCodes.AiInvalidOutput);
            }

            return AiOutputValidator.Validate(choice.GetProperty("message").GetProperty("content").GetString()!, request);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiFailure(ErrorCodes.AiTimeout);
        }
        catch (HttpRequestException)
        {
            throw new AiFailure(ErrorCodes.AiUnavailable);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        {
            throw new AiFailure(ErrorCodes.AiInvalidOutput);
        }
    }

    private static async Task<string> ClassifyFailure(HttpResponseMessage response, CancellationToken ct)
    {
        // Never forward provider messages: they can contain credentials or user content.
        string? code = null;
        try
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (body.RootElement.ValueKind == JsonValueKind.Object &&
            body.RootElement.TryGetProperty("error", out var error) &&
            error.ValueKind == JsonValueKind.Object &&
            error.TryGetProperty("code", out var value) &&
            value.ValueKind == JsonValueKind.String)
            {
                code = value.GetString();
            }
        }
        catch (JsonException)
        { /* Some compatible providers return plain text errors. */
        }

        return ((int)response.StatusCode, code) switch
        {
            (401, _) => ErrorCodes.AiAuthentication,
            (403, _) => ErrorCodes.AiAccessDenied,
            (429, "insufficient_quota" or "billing_hard_limit_reached") => ErrorCodes.AiQuota,
            (429, _) => ErrorCodes.AiRateLimited,
            (400 or 404, "model_not_found") => ErrorCodes.AiModel,
            (400 or 422, _) => ErrorCodes.AiRequestInvalid,
            _ => ErrorCodes.AiUnavailable
        };
    }
}
