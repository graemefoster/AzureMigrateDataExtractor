namespace AzureMigrateDataExtractor;

internal class Machine
{
    public required string Id { get; init; }
    public List<string> IpAddresses { get; init; } = new();
    public List<Application> Applications { get; } = new();
    public List<string> Features { get; } = new();
    public required string DisplayName { get; init; }
    public required string PowerStatus { get; init; }
    public required string Name { get; set; }
}