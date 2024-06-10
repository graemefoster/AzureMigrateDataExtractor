namespace AzureMigrateDataExtractor;

internal class Options
{
    public required string TenantId { get; set; }
    public required string OutputPath { get; set; }
    public required string SubscriptionId { get; set; }
    public required string ResourceGroupName { get; set; }
    public required string AzureMigrateProjectName { get; set; }
}