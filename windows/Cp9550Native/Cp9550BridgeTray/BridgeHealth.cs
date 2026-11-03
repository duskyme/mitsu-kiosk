namespace Cp9550BridgeTray;

internal enum BridgeHealth
{
    /// <summary>Port bound and at least one SpoolBridge process is alive.</summary>
    Healthy,
    /// <summary>SpoolBridge process(es) exist but 127.0.0.1:port is free (hung/zombie).</summary>
    ZombieProcess,
    /// <summary>Port is held but no Cp9550SpoolBridge process was found.</summary>
    PortOrphan,
    /// <summary>No process and port is free.</summary>
    Stopped,
}

internal readonly record struct BridgeHealthReport(
    BridgeHealth Health,
    int ProcessCount,
    bool PortInUse,
    int? PrimaryPid);
