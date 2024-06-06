﻿using CsvHelper.Configuration.Attributes;

namespace AzureMigrateDataExtractor;

internal class MachineDatabaseInventory
{
    [Index(0)]public required string MachineId { get; init; }
    [Index(1)]public required string SqlServerName { get; set; }
    [Index(2)]public required string Edition { get; set; }
    [Index(3)]public required bool IsServerHighAvailabilityEnabled { get; set; }
    [Index(4)]public required int LogicalCpuCount { get; set; }
    [Index(5)]public required string HostName { get; set; }
    [Index(6)]public required string Version { get; set; }
    [Index(7)]public required string DisplayName { get; init; }
    [Index(8)]public required string DatabaseName { get; init; }
    [Index(9)]public required bool IsDatabaseHighlyAvailable { get; set; }
    [Index(10)]public required int SizeInMb { get; set; }
}