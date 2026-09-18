using Nagapie.BraindumpLite.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddNagapieServices();
var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<ApiRequestMiddleware>();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.MapNagapieEndpoints();
app.MapFallbackToFile("index.html");
app.Run();
public partial class Program
{
}
