namespace AzureMigrateDataExtractor;

internal class SqlServer
{
    public required string MachineId { get; init; }
    public required string Version { get; init; }
    public required int LogicalCpuCount { get; init; }
    public required string Edition { get; init; }
    public required bool IsHighAvailabilityEnabled { get; init; }
    public required string HostName { get; init; }
    public required string SqlServerName { get; init; }
    
}