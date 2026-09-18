using System.Threading.RateLimiting;
using System.Text.Json;
using Nagapie.BraindumpLite.Api;
using Nagapie.BraindumpLite.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 65536);
// Payhip includes the license in its query. Disable outgoing HTTP logging entirely.
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
builder.Services.AddHttpClient<IAiBrainDumpProcessor, AiProcessor>(c => c.MaxResponseContentBufferSize = 262144);
builder.Services.AddHttpClient("payhip", c => { c.Timeout = TimeSpan.FromSeconds(15); c.MaxResponseContentBufferSize = 32768; });
builder.Services.AddSingleton<UnlockTokens>();
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.OnRejected = async (context, ct) => await context.HttpContext.Response.WriteAsJsonAsync(new ApiError("RATE_LIMITED", context.HttpContext.TraceIdentifier), ct);
    o.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (context, next) => {
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    try { await next(); }
    catch (BadHttpRequestException ex) {
        if (!context.Response.HasStarted) {
            context.Response.StatusCode = ex.StatusCode;
            await context.Response.WriteAsJsonAsync(new ApiError("INVALID_INPUT", context.TraceIdentifier));
        }
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (Exception) {
        if (!context.Response.HasStarted) {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new ApiError("UNEXPECTED_ERROR", context.TraceIdentifier));
        }
        app.Logger.LogWarning("Request failed {CorrelationId}", context.TraceIdentifier);
    }
    finally {
        if (context.Request.Path.StartsWithSegments("/api")) app.Logger.LogInformation("API {Path} status {Status} duration {ElapsedMs}ms correlation {CorrelationId}", context.Request.Path.Value, context.Response.StatusCode, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, context.TraceIdentifier);
    }
});
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/config", (IConfiguration c) => new PublicConfiguration(c.GetValue<bool>("Access:PaywallEnabled"), 3, "€ 4,99", c["Payhip:ProductUrl"],
    !string.IsNullOrWhiteSpace(c["AiProvider:ApiKey"]) && !string.IsNullOrWhiteSpace(c["AiProvider:Model"]), c["AiProvider:DisplayName"] ?? "OpenAI", c["AiProvider:PrivacyUrl"] ?? "https://openai.com/policies/privacy-policy/"));
app.MapPost("/api/dumps/process", async (ProcessDumpRequest request, IAiBrainDumpProcessor ai, UnlockTokens tokens, IConfiguration config, HttpContext context) => {
    if (!DumpValidation.IsValid(request)) return Results.BadRequest(new ApiError("INVALID_INPUT", context.TraceIdentifier));
    // Deliberately soft accountless limit; the local count is not fraud-resistant.
    if (config.GetValue<bool>("Access:PaywallEnabled") && request.SuccessfulDumpCount >= 3 && !tokens.Verify(request.UnlockToken))
        return Results.Json(new ApiError("PAYWALL_REQUIRED", context.TraceIdentifier), statusCode: 402);
    try { return Results.Ok(await ai.ProcessAsync(request, context.RequestAborted)); }
    catch (AiFailure error) { return Results.Json(new ApiError(error.Code, context.TraceIdentifier), statusCode: 503); }
}).RequireRateLimiting("api");
app.MapPost("/api/licenses/verify", async (VerifyLicenseRequest request, IHttpClientFactory factory, UnlockTokens tokens, IConfiguration config, HttpContext context) => {
    if (string.IsNullOrWhiteSpace(request.LicenseKey) || request.LicenseKey.Length > 100) return Results.BadRequest(new ApiError("LICENSE_INVALID", context.TraceIdentifier));
    if (string.IsNullOrWhiteSpace(config["Payhip:ProductSecret"]) || (config["UnlockTokens:SigningKey"]?.Length ?? 0) < 32)
        return Results.Json(new ApiError("LICENSE_UNAVAILABLE", context.TraceIdentifier), statusCode: 503);
    try {
        using var message = new HttpRequestMessage(HttpMethod.Get, "https://payhip.com/api/v2/license/verify?license_key=" + Uri.EscapeDataString(request.LicenseKey.Trim()));
        message.Headers.Add("product-secret-key", config["Payhip:ProductSecret"]);
        using var response = await factory.CreateClient("payhip").SendAsync(message, context.RequestAborted);
        if (!response.IsSuccessStatusCode) return Results.Json(new ApiError("LICENSE_UNAVAILABLE", context.TraceIdentifier), statusCode: 503);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.RequestAborted));
        var valid = doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True;
        return Results.Ok(new VerifyLicenseResponse(valid, valid ? tokens.Issue(request.LicenseKey.Trim()) : null));
    } catch (JsonException) { return Results.Ok(new VerifyLicenseResponse(false, null)); }
    catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { return Results.Json(new ApiError("LICENSE_UNAVAILABLE", context.TraceIdentifier), statusCode: 503); }
}).RequireRateLimiting("api");
app.Map("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");
app.Run();
public partial class Program { }
