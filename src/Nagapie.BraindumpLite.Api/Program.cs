using Nagapie.BraindumpLite.Api;
using Nagapie.BraindumpLite.Api.Accounts;
using Nagapie.BraindumpLite.Api.Data;

var builder = WebApplication.CreateBuilder(args);
builder.AddNagapieServices();
builder.AddSqlAccounts();
var app = builder.Build();
var migrateOnly = args.Contains("--migrate");
if (migrateOnly || builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>()
        .MigrateAsync(app.Lifetime.ApplicationStopping);
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
app.MapAccountEndpoints();
app.MapUserDataEndpoints();
app.MapNagapieEndpoints();
app.MapFallbackToFile("index.html");
app.Run();
public partial class Program
{
}
