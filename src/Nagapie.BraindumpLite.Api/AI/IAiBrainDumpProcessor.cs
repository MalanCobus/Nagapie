using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Api;

public interface IAiBrainDumpProcessor
{
    Task<ProcessDumpResponse> ProcessAsync(ProcessDumpRequest request, CancellationToken ct);
}
