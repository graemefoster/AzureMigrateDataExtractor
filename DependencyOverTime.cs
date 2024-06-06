﻿using CsvHelper.Configuration.Attributes;

namespace AzureMigrateDataExtractor;

internal class DependencyOverTime
{
    [Index(0)][Name("Source server name")] public required string SourceServerName { get; init; }
    [Index(1)][Name("Source IP")] public required string SourceIp { get; init; }
    [Index(2)][Name("Source application")] public required string? SourceApplication { get; init; }
    [Index(3)][Name("Source process")] public required string? SourceProcess { get; init; }
    [Index(4)][Name("Destination server name")] public required string DestinationServerName { get; init; }
    [Index(5)][Name("Destination IP")] public required string DestinationIp { get; init; }
    [Index(6)][Name("Destination application")] public required string? DestinationApplication { get; init; }
    [Index(7)][Name("Destination process")] public required string? DestinationProcess { get; init; }
    [Index(8)][Name("Destination port")] public required int? DestinationPort { get; init; }
}