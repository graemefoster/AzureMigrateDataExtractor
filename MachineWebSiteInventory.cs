using CsvHelper.Configuration.Attributes;

namespace AzureMigrateDataExtractor;

internal class MachineWebSiteInventory
{
    [Index(0)]public required string MachineId { get; init; }
    [Index(1)]public required string WebServerType { get; init; }
    [Index(2)]public required string WebServerName { get; init; }
    [Index(3)]public required string DisplayName { get; init; }
    [Index(4)]public required string FrameworkName { get; init; }
    [Index(5)]public required string FrameworkVersion { get; init; }
}