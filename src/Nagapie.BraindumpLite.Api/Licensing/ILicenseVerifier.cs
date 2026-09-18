using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Api;

public interface ILicenseVerifier
{
    Task<VerifyLicenseResponse> VerifyAsync(string licenseKey, CancellationToken cancellationToken);
}
