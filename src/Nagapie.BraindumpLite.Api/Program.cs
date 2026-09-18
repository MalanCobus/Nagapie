using Microsoft.EntityFrameworkCore;
using Nagapie.BraindumpLite.Api;
using Nagapie.BraindumpLite.Api.Accounts;
using Nagapie.BraindumpLite.Api.Data;

var builder = WebApplication.CreateBuilder(args);
builder.AddNagapieServices();
builder.AddSqlAccounts();
var app = builder.Build();
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<NagapieDbContext>().Database.MigrateAsync();
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
