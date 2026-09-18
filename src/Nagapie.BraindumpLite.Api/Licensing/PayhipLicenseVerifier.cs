using System.Text.Json;
using Microsoft.Extensions.Options;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Api;

public sealed class PayhipLicenseVerifier(HttpClient http, IOptions<PayhipOptions> payhipOptions, IOptions<UnlockTokenOptions> tokenOptions, IUnlockTokenService tokens) : ILicenseVerifier
{
    public async Task<VerifyLicenseResponse> VerifyAsync(string licenseKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payhipOptions.Value.ProductSecret) || tokenOptions.Value.SigningKey.Length < 32)
        {
            throw new LicenseUnavailableException();
        }

        try
        {
            var normalizedKey = licenseKey.Trim();
            using var message = new HttpRequestMessage(HttpMethod.Get, "https://payhip.com/api/v2/license/verify?license_key=" + Uri.EscapeDataString(normalizedKey));
            message.Headers.Add("product-secret-key", payhipOptions.Value.ProductSecret);
            using var response = await http.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new LicenseUnavailableException();
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var valid = document.RootElement.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("enabled", out var enabled) &&
            enabled.ValueKind == JsonValueKind.True;
            return new VerifyLicenseResponse(valid, valid ? tokens.Issue(normalizedKey) : null);
        }
        catch (JsonException)
        {
            return new VerifyLicenseResponse(false, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            throw new LicenseUnavailableException();
        }
    }
}
