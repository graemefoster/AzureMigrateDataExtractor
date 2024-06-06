using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using CsvHelper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AzureMigrateDataExtractor;

internal class DataExtractor : IHostedService
{
    private readonly ILogger<DataExtractor> _logger;
    private readonly Options _options;
    private readonly HttpClient _httpClient;

    const string EarlierApiVersion = "api-version=2020-06-01-preview";
    const string ApiVersion = "api-version=2023-06-06";
    const string Root = "https://management.azure.com";

    public DataExtractor(IHttpClientFactory httpClientFactory, IOptions<Options> options, ILogger<DataExtractor> logger)
    {
        _logger = logger;
        _options = options.Value;
        _httpClient = httpClientFactory.CreateClient("arm");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var credential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
        {
            TenantId = _options.TenantId
        });

        _logger.LogInformation("Signing into Azure using Azure CLI");
        var token = await credential.GetTokenAsync(new TokenRequestContext(["https://management.azure.com/"]),
            cancellationToken);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        Directory.CreateDirectory(_options.OutputPath);

        await ExtractData();
    }

    private async Task ExtractData()
    {
        var subscriptionId = _options.SubscriptionId;
        var resourceGroupName = _options.ResourceGroupName;
        var migrateProjectName = _options.AzureMigrateProjectName;

        var api =
            $"{Root}/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Migrate/migrateProjects/{migrateProjectName}/solutions?{EarlierApiVersion}";

        await foreach (var project in FetchWithNextLink(api))
        {
            if (project["properties"]!["summary"]!.Value<int>("discoveredCount") == 0) continue;

            var masterSiteInfo = await _httpClient.GetJsonAsync<JObject>(
                $"{Root}{project["properties"]!["details"]!["extendedDetails"]!.Value<string>("masterSiteId")}?{ApiVersion}");

            var projectName = project.Value<string>("name")!;

            _logger.LogInformation($"Processing Project {projectName}");


            await using var machineWebsiteWriter =
                new CsvWriter(
                    File.CreateText(Path.Combine(_options.OutputPath,
                        $"{DateTime.Now:yyyy-MM-dd}-{migrateProjectName}-{projectName}-machines-websites.csv")),
                    CultureInfo.InvariantCulture);
            machineWebsiteWriter.WriteHeader<MachineWebSiteInventory>();

            await using var machineDatabaseWriter =
                new CsvWriter(
                    File.CreateText(Path.Combine(_options.OutputPath,
                        $"{DateTime.Now:yyyy-MM-dd}-{migrateProjectName}-{projectName}-machines-databases.csv")),
                    CultureInfo.InvariantCulture);
            machineDatabaseWriter.WriteHeader<MachineDatabaseInventory>();

            await using var machineSoftwareWriter =
                new CsvWriter(
                    File.CreateText(
                        Path.Combine(_options.OutputPath,
                            $"{DateTime.Now:yyyy-MM-dd}-{migrateProjectName}-{projectName}-machine-software.csv")),
                    CultureInfo.InvariantCulture);
            machineSoftwareWriter.WriteHeader<MachineSoftwareInventory>();

            await using var dependenciesWriter =
                new CsvWriter(
                    File.CreateText(
                        Path.Combine(_options.OutputPath,
                            $"{DateTime.Now:yyyy-MM-dd}-{migrateProjectName}-{projectName}-machine-dependencies.csv")),
                    CultureInfo.InvariantCulture);
            dependenciesWriter.WriteHeader<DependencyOverTime>();

            foreach (var nestedSites in masterSiteInfo["properties"]!["nestedSites"]!)
            {
                var nestedSite =
                    await _httpClient.GetJsonAsync<JObject>($"{Root}{nestedSites.Value<string>()}?{ApiVersion}");

                if (nestedSite.Value<string>("type") == "Microsoft.OffAzure/MasterSites/SqlSites")
                {
                    _logger.LogInformation($"Processing Sql Site {projectName}");

                    var sqlServers = new Dictionary<string, SqlServer>();
                    await foreach (var sqlServer in FetchWithNextLink(
                                       $"{Root}{nestedSites.Value<string>()}/sqlServers?{ApiVersion}"))
                    {
                        sqlServers[sqlServer.Value<string>("id")!] = new SqlServer()
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

                    await foreach (var database in FetchWithNextLink(
                                       $"{Root}{nestedSites.Value<string>()}/sqlDatabases?{ApiVersion}"))
                    {
                        _logger.LogInformation($"Processing Sql Database {projectName}");

                        var sqlServer = sqlServers[database["properties"]!.Value<string>("sqlServerArmId")!];
                        machineDatabaseWriter.WriteRecord(new MachineDatabaseInventory()
                        {
                            DatabaseName = database["properties"]!.Value<string>("databaseName")!,
                            MachineId = sqlServer.MachineId,
                            Edition = sqlServer.Edition,
                            Version = sqlServer.Version,
                            DisplayName = "",
                            HostName = sqlServer.HostName,
                            LogicalCpuCount = sqlServer.LogicalCpuCount,
                            SqlServerName = sqlServer.SqlServerName,
                            IsServerHighAvailabilityEnabled = sqlServer.IsHighAvailabilityEnabled,
                            SizeInMb = database["properties"]!.Value<int>("sizeMB"),
                            IsDatabaseHighlyAvailable =
                                database["properties"]!.Value<bool>("isDatabaseHighlyAvailable"),
                        });
                    }
                }
                else if (nestedSite.Value<string>("type") == "Microsoft.OffAzure/MasterSites/WebAppSites")
                {
                    _logger.LogInformation($"Processing Web App Sites {projectName}");

                    await foreach (var webApplication in FetchWithNextLink(
                                       $"{Root}{nestedSites.Value<string>()}/WebApplications?{ApiVersion}"))
                    {
                        machineWebsiteWriter.WriteRecord(new MachineWebSiteInventory()
                        {
                            MachineId = webApplication["properties"]!["machineArmIds"]?.FirstOrDefault()
                                ?.Value<string>() ?? "NA",
                            DisplayName = webApplication["properties"]!.Value<string>("displayName")!,
                            FrameworkName = webApplication["properties"]!["frameworks"]?.FirstOrDefault()
                                ?.Value<string>("name") ?? "NA",
                            FrameworkVersion = webApplication["properties"]!["frameworks"]?.FirstOrDefault()
                                ?.Value<string>("version") ?? "NA",
                            WebServerType = webApplication["properties"]!.Value<string>("serverType")!,
                            WebServerName = webApplication["properties"]!.Value<string>("webServerName")!,
                        });
                    }
                }
            }


            foreach (var appliance in masterSiteInfo["properties"]!["sites"]!)
            {
                var applianceName = appliance.Value<string>();

                await foreach (var machine in FetchWithNextLink(
                                   $"{Root}{applianceName}/machines?{ApiVersion}"))
                {
                    _logger.LogInformation($"Processing Machine {machine["properties"]!.Value<string>("displayName")}");

                    var machineObj = new Machine()
                    {
                        Id = machine.Value<string>("id")!,
                        Name = machine.Value<string>("name")!,
                        DisplayName = machine["properties"]!.Value<string>("displayName")!,
                        PowerStatus = machine["properties"]!.Value<string>("powerStatus")!,
                        IpAddresses = machine["properties"]!["networkAdapters"]!
                            .SelectMany(x => x["ipAddressList"]!.Values<string>().Select(ip => ip!)).ToList()
                    };

                    var apps = await _httpClient.GetJsonAsync<JObject>(
                        $"{Root}{machine.Value<string>("id")}/applications?{ApiVersion}");
                    foreach (var app in apps["properties"]!["appsAndRoles"]!["applications"]!)
                    {
                        machineObj.Applications.Add(new Application()
                        {
                            Name = app.Value<string>("name")!,
                            Provider = app.Value<string>("provider")!,
                            Version = app.Value<string>("version")!
                        });
                    }

                    foreach (var app in apps["properties"]!["appsAndRoles"]!["features"]!)
                    {
                        machineObj.Features.Add(app.Value<string>("name")!);
                    }

                    await machineSoftwareWriter.WriteRecordsAsync(
                        machineObj.Applications.Select(x =>
                            new MachineSoftwareInventory()
                            {
                                ApplicationName = x.Name,
                                ApplicationProvider = x.Provider,
                                ApplicationVersion = x.Version,
                                DisplayName = machineObj.DisplayName,
                                MachineId = machineObj.Name,
                                PowerStatus = machineObj.PowerStatus,
                                FeatureName = null
                            }).Concat(
                            machineObj.Features.Select(x =>
                                new MachineSoftwareInventory()
                                {
                                    ApplicationName = null,
                                    ApplicationProvider = null,
                                    ApplicationVersion = null,
                                    DisplayName = machineObj.DisplayName,
                                    MachineId = machineObj.Name,
                                    PowerStatus = machineObj.PowerStatus,
                                    FeatureName = x
                                })));
                }

                //get the dependency export
                _logger.LogInformation($"Fetching Dependencies");

                var dependenciesResponse = await _httpClient.PostJsonAsync(
                    $"{Root}{applianceName}/exportDependencies?{ApiVersion}",
                    new
                    {
                        endTime = DateTimeOffset.UtcNow,
                        startTime = DateTimeOffset.UtcNow.AddDays(-7)
                    });

                var statusUri = dependenciesResponse.Headers.GetValues("Azure-AsyncOperation").First();

                string? status;
                JObject? dependencies;
                do
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    dependencies = await _httpClient.GetJsonAsync<JObject>($"{statusUri}");
                    status = dependencies.Value<string>("status")!;
                    _logger.LogInformation("Waiting for Dependencies extract");
                } while (status == "Running");

                if (status == "Succeeded")
                {
                    _logger.LogInformation("Downloading Dependencies extract");

                    var resultJson =
                        JsonConvert.DeserializeObject<JObject>(dependencies["properties"]!.Value<string>("result")!)!;
                    using var dependenciesCsvReader =
                        new CsvReader(
                            new StreamReader(
                                await new HttpClient().GetStreamAsync(resultJson.Value<string>("SASUri")!)),
                            CultureInfo.InvariantCulture);

                    var grouped = new HashSet<DependencyOverTime>();
                    await foreach (var record in dependenciesCsvReader.GetRecordsAsync<Dependency>())
                    {
                        if (record.SourceProcess == "System Idle Process") continue;

                        var dependency = new DependencyOverTime()
                        {
                            SourceServerName = record.SourceServerName,
                            SourceIp = record.SourceIp,
                            SourceApplication = record.SourceApplication,
                            SourceProcess = record.SourceProcess,
                            DestinationServerName = record.DestinationServerName,
                            DestinationIp = record.DestinationIp,
                            DestinationApplication = record.DestinationApplication,
                            DestinationProcess = record.DestinationProcess,
                            DestinationPort = record.DestinationPort
                        };
                        grouped.Add(dependency);
                    }

                    await dependenciesWriter.WriteRecordsAsync(grouped);
                    await dependenciesWriter.FlushAsync();
                }
            }
        }
        _logger.LogInformation($"Finished extracting data. Output files are in {_options.OutputPath}");
    }

    private async IAsyncEnumerable<JToken> FetchWithNextLink(string api)
    {
        var projects = await _httpClient.GetJsonAsync<JObject>(api);
        foreach (var project in projects["value"]!)
        {
            yield return project;
        }

        while (!string.IsNullOrWhiteSpace(projects.Value<string>("nextLink")))
        {
            projects = await _httpClient.GetJsonAsync<JObject>(projects.Value<string>("nextLink")!);
            foreach (var project in projects["value"]!)
            {
                yield return project;
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}