using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Api;

public sealed class LicenseUnavailableException() : Exception(ErrorCodes.LicenseUnavailable);
