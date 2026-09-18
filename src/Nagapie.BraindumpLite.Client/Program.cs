using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Nagapie.BraindumpLite.Client;
using Nagapie.BraindumpLite.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddScoped<AccountContext>();
builder.Services.AddScoped(services => new HttpClient(new AccountHttpHandler(services.GetRequiredService<AccountContext>())
{
    InnerHandler = new HttpClientHandler()
})
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromSeconds(25)
});
builder.Services.AddScoped<AccountClient>();
builder.Services.AddLocalization();
builder.Services.AddScoped<Microsoft.Extensions.Localization.IStringLocalizer<Nagapie.BraindumpLite.Client.Resources.AppResources>, AppLocalizer>();
builder.Services.AddScoped<IUserDataStore, SqlUserDataStore>();
builder.Services.AddScoped<INagapieApiClient, NagapieApiClient>();
builder.Services.AddScoped<AppState>();
await builder.Build().RunAsync();
