using CsvHelper.Configuration.Attributes;

namespace AzureMigrateDataExtractor;

internal class MachineSoftwareInventory
{
    [Index(0)]public required string MachineId { get; init; }
    [Index(1)]public required string PowerStatus { get; init; }
    [Index(2)]public required string DisplayName { get; init; }
    [Index(3)]public required string? ApplicationName { get; init; }
    [Index(4)]public required string? ApplicationVersion { get; init; }
    [Index(5)]public required string? ApplicationProvider { get; init; }
    [Index(6)]public required string? FeatureName { get; init; }
}