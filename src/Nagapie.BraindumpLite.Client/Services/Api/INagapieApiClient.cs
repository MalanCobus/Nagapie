using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Client.Services;

public interface INagapieApiClient
{
    Task<PublicConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken);
    Task<List<SavedDumpResponse>> GetSavedDumpsAsync(CancellationToken cancellationToken = default, int page = 0);
    Task<ProcessDumpResponse> ProcessDumpAsync(ProcessDumpRequest request, CancellationToken cancellationToken);
    Task<VerifyLicenseResponse> VerifyLicenseAsync(string licenseKey, CancellationToken cancellationToken = default);
}
