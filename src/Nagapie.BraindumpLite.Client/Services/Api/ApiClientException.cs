namespace Nagapie.BraindumpLite.Client.Services;

public sealed class ApiClientException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
