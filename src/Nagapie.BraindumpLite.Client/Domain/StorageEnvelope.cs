namespace Nagapie.BraindumpLite.Client.Domain;

public sealed record StorageEnvelope<T>(int SchemaVersion, T Data);
