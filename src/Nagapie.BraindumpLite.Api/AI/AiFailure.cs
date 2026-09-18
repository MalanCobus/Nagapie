namespace Nagapie.BraindumpLite.Api;

public sealed class AiFailure(string code) : Exception(code)
{
    public string Code { get; } = code;
}
