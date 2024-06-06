using Newtonsoft.Json.Linq;

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

    internal static SqlServer FromJToken(JToken sqlServer)
    {
        return new SqlServer()
        {
            Edition = sqlServer["properties"]!.Value<string>("edition")!,
            Version = sqlServer["properties"]!.Value<string>("version")!,
            HostName = sqlServer["properties"]!.Value<string>("hostName")!,
            IsHighAvailabilityEnabled =
                sqlServer["properties"]!.Value<bool>("isHighAvailabilityEnabled"),
            LogicalCpuCount = sqlServer["properties"]!.Value<int>("logicalCpuCount"),
            MachineId = sqlServer["properties"]!["machineOverviewList"]!.FirstOrDefault()
                ?.Value<string>("extendedMachineId") ?? "NA",
            SqlServerName = sqlServer["properties"]!.Value<string>("sqlServerName")!,
        };
    }
    
}