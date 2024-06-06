using CsvHelper.Configuration.Attributes;
using Newtonsoft.Json.Linq;

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

    public static MachineSoftwareInventory FromMachineAndApplication(Machine machineObj, Application application)
    {
        return new MachineSoftwareInventory()
        {
            ApplicationName = application.Name,
            ApplicationProvider = application.Provider,
            ApplicationVersion = application.Version,
            DisplayName = machineObj.DisplayName,
            MachineId = machineObj.Name,
            PowerStatus = machineObj.PowerStatus,
            FeatureName = null
        };
    }

    public static MachineSoftwareInventory FromMachineAndFeature(Machine machineObj, string feature)
    {
        return new MachineSoftwareInventory()
        {
            ApplicationName = null,
            ApplicationProvider = null,
            ApplicationVersion = null,
            DisplayName = machineObj.DisplayName,
            MachineId = machineObj.Name,
            PowerStatus = machineObj.PowerStatus,
            FeatureName = feature
        };
    }
}