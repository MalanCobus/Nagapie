using Nagapie.BraindumpLite.Contracts;
namespace Nagapie.BraindumpLite.Client.Services;

public sealed class AccountContext
{
    public AccountSession? Session
    {
        get; private set;
    }
    public bool IsAuthenticated => Session?.UserId is not null;
    public event Action? Changed;

    public void Set(AccountSession? session)
    {
        Session = session;
        Changed?.Invoke();
    }
}

