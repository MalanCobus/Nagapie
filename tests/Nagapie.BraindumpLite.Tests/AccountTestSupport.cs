using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nagapie.BraindumpLite.Api.Data;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Tests;

internal static class AccountTestSupport
{
    public static void UseTestDatabase(IServiceCollection services)
    {
        services.RemoveAll<DbContextOptions<NagapieDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<NagapieDbContext>>();
        services.AddSingleton(_ =>
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            return connection;
        });
        services.AddDbContext<NagapieDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
    }

    public static async Task<HttpClient> CreateUserAsync(WebApplicationFactory<Program> factory, string? email = null)
    {
        var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NagapieDbContext>().Database.EnsureCreatedAsync();
        await RefreshAsync(client);
        using var response = await client.PostAsJsonAsync("/api/account/register",
            new CredentialsRequest(email ?? $"{Guid.NewGuid():N}@example.com", "A long test password!"));
        response.EnsureSuccessStatusCode();
        await RefreshAsync(client);
        return client;
    }

    public static async Task<AccountSession> RefreshAsync(HttpClient client)
    {
        var session = (await client.GetFromJsonAsync<AccountSession>("/api/account/session"))!;
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.RequestToken);
        client.DefaultRequestHeaders.Remove("X-Account-Id");
        if (session.UserId is not null)
        {
            client.DefaultRequestHeaders.Add("X-Account-Id", session.UserId);
        }

        return session;
    }
}
