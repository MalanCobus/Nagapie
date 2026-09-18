using Nagapie.BraindumpLite.Api;
using Nagapie.BraindumpLite.Api.Accounts;
using Nagapie.BraindumpLite.Api.Data;

var builder = WebApplication.CreateBuilder(args);
builder.AddNagapieServices();
builder.AddSqlAccounts();
builder.Services.AddHealthChecks().AddCheck<DatabaseReadiness>("database");
var app = builder.Build();
var migrateOnly = args.Contains("--migrate");
if (migrateOnly || (app.Environment.IsDevelopment() && builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false)))
{
    await using var scope = app.Services.CreateAsyncScope();
    try
    {
        await scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>()
            .MigrateAsync(app.Lifetime.ApplicationStopping);
    }
    catch when (migrateOnly)
    {
        // The migrator already emitted safe diagnostics. Do not print raw SQL exceptions.
        Environment.ExitCode = 1;
        return;
    }
}
if (migrateOnly)
{
    return;
}
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<ApiRequestMiddleware>();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapHealthChecks("/health/ready");
app.MapAccountEndpoints();
app.MapUserDataEndpoints();
app.MapNagapieEndpoints();
app.MapFallbackToFile("index.html");
app.Run();
public partial class Program
{
}
