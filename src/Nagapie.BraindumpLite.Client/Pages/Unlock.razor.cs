using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Client.Pages;

public partial class Unlock
{
    private string key = "";
    private bool success;
    private Task VerifyAsync() => Run(async () =>
    {
        var result = await Api.VerifyLicenseAsync(key);
        if (result is not { IsValid: true, UnlockToken: not null })
        {
            Error = ErrorCodes.LicenseInvalid;
            return;
        }

        await State.UnlockAsync(result.UnlockToken);
        key = "";
        success = true;
    });
}
