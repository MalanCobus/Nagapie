using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nagapie.BraindumpLite.Api.Data;

namespace Nagapie.BraindumpLite.Api;

public static class AccountRegistration
{
    public static void AddSqlAccounts(this WebApplicationBuilder builder)
    {
        var connection = builder.Configuration.GetConnectionString("Nagapie");
        if (string.IsNullOrWhiteSpace(connection))
        {
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException("Configure ConnectionStrings:Nagapie before starting the hosted app.");
            }

            connection = @"Server=(localdb)\MSSQLLocalDB;Database=Nagapie;Trusted_Connection=True;TrustServerCertificate=True";
        }

        builder.Services.AddDbContext<NagapieDbContext>(options =>
            options.UseSqlServer(connection));

        builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<NagapieDbContext>().AddDefaultTokenProviders();

        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "Nagapie.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        builder.Services.AddScoped<IUserDocumentStore, SqlUserDocumentStore>();
        builder.Services.AddScoped<IDatabaseMigrator, SqlDatabaseMigrator>();
        builder.Services.AddScoped<LegacyDataImporter>();
        builder.Services.AddScoped<IRelationalDataStore, RelationalDataStore>();
    }
}
