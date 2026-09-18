namespace Nagapie.BraindumpLite.Api;

public interface IUnlockTokenService
{
    string Issue(string license);
    bool Verify(string? token);
}
