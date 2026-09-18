using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Client.Services;

public interface INagapieApiClient
{
    Task<PublicConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken);
    Task<ProcessDumpResponse> ProcessDumpAsync(ProcessDumpRequest request, CancellationToken cancellationToken);
    Task<VerifyLicenseResponse> VerifyLicenseAsync(string licenseKey, CancellationToken cancellationToken = default);
}
