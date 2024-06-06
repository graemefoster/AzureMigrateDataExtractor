// See https://aka.ms/new-console-template for more information

using System;
using System.CommandLine;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace AzureMigrateDataExtractor;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public static class Program
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public static Task Main(string[] args)
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    {
        var tenantIdOption = new Option<string>(name: "--tenant-id");
        var subscriptionIdOption = new Option<string>(name: "--subscription-id");
        var resourceGroupNameOption = new Option<string>(name: "--resource-group-name");
        var azureMigrateProjectNameOption = new Option<string>(name: "--azure-migrate-project-name");
        var outputPathOption = new Option<string>(name: "--output-path");

        Console.WriteLine("Azure Migrate data extractor");
        
        var rootCommand = new RootCommand("Azure Migrate Data Extractor");
        rootCommand.AddOption(tenantIdOption);
        rootCommand.AddOption(subscriptionIdOption);
        rootCommand.AddOption(resourceGroupNameOption);
        rootCommand.AddOption(azureMigrateProjectNameOption);
        rootCommand.AddOption(outputPathOption);

        rootCommand.SetHandler(
            (tenantId, subscriptionId, resourceGroupName, azureMigrateProjectName, outputPath) => RunExtractor(
                new Options()
                {
                    TenantId = tenantId,
                    SubscriptionId = subscriptionId,
                    ResourceGroupName = resourceGroupName,
                    OutputPath = outputPath,
                    AzureMigrateProjectName = azureMigrateProjectName
                })
            , tenantIdOption, subscriptionIdOption, resourceGroupNameOption, azureMigrateProjectNameOption,
            outputPathOption);

        return rootCommand.InvokeAsync(args);
    }

    private static async Task RunExtractor(Options options)
    {
        
        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureLogging(lb =>
            {
                lb.ClearProviders();
                lb.SetMinimumLevel(LogLevel.Information);
                lb.AddFilter((_, category, _) => category?.StartsWith("AzureMigrateDataExtractor") ?? false);
                lb.AddSimpleConsole(x =>
                {
                    x.ColorBehavior = LoggerColorBehavior.Enabled;
                    x.SingleLine = true;
                });
            })
            .ConfigureServices(services =>
            {
                services.AddHttpClient("arm").AddStandardResilienceHandler();
                services.AddHostedService<DataExtractor>();
                services.Configure<Options>(opt =>
                {
                    opt.TenantId = options.TenantId;
                    opt.SubscriptionId = options.SubscriptionId;
                    opt.ResourceGroupName = options.ResourceGroupName;
                    opt.AzureMigrateProjectName = options.AzureMigrateProjectName;
                    opt.OutputPath = options.OutputPath;
                });
            });

        var host = hostBuilder.Build();

        await host.StartAsync();
        await host.StopAsync();
    }
}