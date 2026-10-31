namespace Gp200.Replica;

// Application is the composition root. Operation scopes carry an explicitly owned
// session through async services/codecs; they never replace another session's state.
internal sealed class DeviceSession
{
    static readonly Lazy<DeviceSession> application = new(() => new());
    static readonly AsyncLocal<DeviceSession?> operation = new();
    internal static DeviceSession Current => operation.Value ?? application.Value;
    readonly Lazy<Catalog> configuredCatalog;
    internal Catalog Catalog => configuredCatalog.Value;
    internal bool Connected {get;set;}
    internal ushort Format {get;set;}
    internal uint Capabilities;
    internal int MaxChunk = 128;
    internal bool UniversalObserved;
    internal DeviceSession()
    {
        configuredCatalog = new(() => Gp200.Replica.Catalog.Configured(AppPreferences.Load()));
    }
    internal Task<DeviceResponse> Read(ReadQuery query, int slot = 0) =>
        ReadOnlyMidi.QueryAsync(query, slot);
    internal Task<DeviceResponse> Exchange(byte command, IReadOnlyList<WireRecord> records,
        Func<IReadOnlyList<WireRecord>, bool>? match = null, int timeoutMs = 3000) =>
        DeviceMidi.ExchangeHardwareAsync(command, records, match, timeoutMs);
    internal Task<DeviceResponse> Write(IReadOnlyList<WireRecord> records) => DeviceMidi.ExchangeHardwareAsync(0x12, records);
    internal IDisposable Enter() => new Scope(this);
    sealed class Scope : IDisposable
    {
        readonly DeviceSession? previous;
        bool disposed;
        internal Scope(DeviceSession owner) { previous = operation.Value; operation.Value = owner; }
        public void Dispose() { if (!disposed) { operation.Value = previous; disposed = true; } }
    }
    internal void Reset()
    {
        Connected=false;Format=0;Capabilities = 0; MaxChunk = 128;
        if (configuredCatalog.IsValueCreated && Catalog.IsMx42)
        { Catalog.InvalidateMx42DeviceNames(); Catalog.ClearDeviceCabMap(); }
    }
}
