using System.Threading.RateLimiting;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Api;

public static class ServiceRegistration
{
    public static WebApplicationBuilder AddNagapieServices(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<AiProviderOptions>(builder.Configuration.GetSection(AiProviderOptions.SectionName));
        builder.Services.Configure<AccessOptions>(builder.Configuration.GetSection(AccessOptions.SectionName));
        builder.Services.Configure<PayhipOptions>(builder.Configuration.GetSection(PayhipOptions.SectionName));
        builder.Services.Configure<UnlockTokenOptions>(builder.Configuration.GetSection(UnlockTokenOptions.SectionName));
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 4 * 1024 * 1024);
        // Payhip includes the license in its query. Disable outgoing HTTP logging entirely.
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
        // Raw EF exceptions may include constraint values; middleware/migrator emit safe details.
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
        builder.Services.AddHttpClient<IAiBrainDumpProcessor, AiProcessor>(c => c.MaxResponseContentBufferSize = 262144);
        builder.Services.AddHttpClient<ILicenseVerifier, PayhipLicenseVerifier>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(15);
            c.MaxResponseContentBufferSize = 32768;
        });
        builder.Services.AddSingleton<UnlockTokens>();
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.OnRejected = async (context, ct) => await context.HttpContext.Response.WriteAsJsonAsync(new ApiError(ErrorCodes.RateLimited, context.HttpContext.TraceIdentifier), ct);
            o.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 15,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });
        builder.Services.AddSingleton<IUnlockTokenService>(services => services.GetRequiredService<UnlockTokens>());
        return builder;
    }
}
