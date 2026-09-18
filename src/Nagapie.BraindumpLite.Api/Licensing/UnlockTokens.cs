using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nagapie.BraindumpLite.Api;

public sealed class UnlockTokens(Microsoft.Extensions.Options.IOptions<UnlockTokenOptions> options) : IUnlockTokenService
{
    private byte[] Key => Encoding.UTF8.GetBytes(options.Value.SigningKey);

    public string Issue(string license)
    {
        if (Key.Length < 32)
        {
            throw new InvalidOperationException("Signing key must contain at least 32 bytes.");
        }

        var licenseHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(license)));
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new
        {
            product = "braindump-lite-web",
            licenseHash,
            issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            version = 1
        }));
        return payload + "." + Convert.ToBase64String(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(payload)));
    }

    public bool Verify(string? token)
    {
        if (Key.Length < 32 || string.IsNullOrWhiteSpace(token) || token.Length > 2048)
        {
            return false;
        }

        try
        {
            var parts = token.Split('.');
            if (parts.Length != 2 || !CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(parts[1]), HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(parts[0]))))
            {
                return false;
            }

            using var doc = JsonDocument.Parse(Convert.FromBase64String(parts[0]));
            return doc.RootElement.GetProperty("product").GetString() == "braindump-lite-web" && doc.RootElement.GetProperty("version").GetInt32() == 1;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }
}
