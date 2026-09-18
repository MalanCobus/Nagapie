namespace Nagapie.BraindumpLite.Api.Data;

public sealed class OperationReceipt
{
    public string UserId { get; set; } = "";
    public Guid Id
    {
        get; set;
    }
    public string RequestHash { get; set; } = "";
    public string ResponseJson { get; set; } = "";
}
