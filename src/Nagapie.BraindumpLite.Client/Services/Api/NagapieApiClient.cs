using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Client.Services;

public sealed class NagapieApiClient(HttpClient http) : INagapieApiClient
{
    public Task<PublicConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken) => http.GetFromJsonAsync<PublicConfiguration>("api/config", cancellationToken);
    public async Task<ProcessDumpResponse> ProcessDumpAsync(ProcessDumpRequest request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("api/dumps/process", request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.PaymentRequired)
        {
            throw new ApiClientException(ErrorCodes.PaywallRequired);
        }

        try
        {
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken);
                throw new ApiClientException(error?.Code ?? ErrorCodes.AiUnavailable);
            }

            var result = await response.Content.ReadFromJsonAsync<ProcessDumpResponse>(cancellationToken);
            if (result is null ||
            result.SourceDumpId != request.SourceDumpId ||
            result.Items is not { Count: > 0 and <= 30 } ||
            result.Items.Any(item => item is null ||
            string.IsNullOrWhiteSpace(item.Text) ||
            item.Text.Length > 500))
            {
                throw new ApiClientException(ErrorCodes.AiInvalidOutput);
            }

            return result;
        }
        catch (JsonException)
        {
            throw new ApiClientException(ErrorCodes.AiInvalidOutput);
        }
    }

    public async Task<VerifyLicenseResponse> VerifyLicenseAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/licenses/verify", new VerifyLicenseRequest(licenseKey), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiClientException(ErrorCodes.LicenseUnavailable);
        }

        try
        {
            var result = await response.Content.ReadFromJsonAsync<VerifyLicenseResponse>(cancellationToken);
            return result ?? throw new ApiClientException(ErrorCodes.LicenseInvalid);
        }
        catch (JsonException)
        {
            throw new ApiClientException(ErrorCodes.LicenseInvalid);
        }
    }
}
