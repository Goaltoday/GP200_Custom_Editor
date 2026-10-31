namespace Gp200.Replica;

internal enum DeviceOperationKind
{
    Query, Volatile, Persistent
}
internal static class DeviceOperations
{
    internal static DeviceOperationKind Integration(byte service, byte operation) => (service, operation) switch
    {
        (0, 0) or (1, 0) or (2, 0 or 1) or (3, 0 or 1) or (4, 0) or (5, 0 or 5) => DeviceOperationKind.Query,
        (1, 1) or (2, 2 or 3 or 4 or 5 or 6) or (5, 1 or 2 or 4) => DeviceOperationKind.Volatile,
        (5, 3) => DeviceOperationKind.Persistent,
        _ => throw new NotSupportedException("Unknown integration operation classification; no command was sent.")
    };
}
