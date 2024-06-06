using System.Globalization;
using System.Net.Http.Headers;
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

        _logger.LogInformation("Signing into Azure");
        var token = await credential.GetTokenAsync(
            new TokenRequestContext(["https://management.azure.com/user_impersonation"]),
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

            var masterSiteInfo = await FetchMasterSite(project);
            var projectName = project.Value<string>("name")!;
            _logger.LogInformation($"Processing Project {projectName}");
            var machines = await ExtractMachinesAndDependencies(masterSiteInfo, projectName);
            await ExtractWebAppsAndSqlDatabases(machines, masterSiteInfo, projectName);
        }

        _logger.LogInformation($"Finished extracting data. Output files are in {_options.OutputPath}");
    }

    private async Task<JObject> FetchMasterSite(JToken project)
    {
        return await _httpClient.GetJsonAsync<JObject>(
            $"{Root}{project["properties"]!["details"]!["extendedDetails"]!.Value<string>("masterSiteId")}?{ApiVersion}");
    }

    private async Task<Dictionary<string, Machine>> ExtractMachinesAndDependencies(JObject masterSiteInfo, string projectName)
    {
        await using var machineWriter =
            new CsvWriter(
                File.CreateText(
                    Path.Combine(_options.OutputPath,
                        $"{DateTime.Now:yyyy-MM-dd}-{projectName}-machines.csv")),
                CultureInfo.InvariantCulture);
        machineWriter.WriteHeader<MachineExport>();
        await machineWriter.NextRecordAsync();

        await using var machineSoftwareWriter =
            new CsvWriter(
                File.CreateText(
                    Path.Combine(_options.OutputPath,
                        $"{DateTime.Now:yyyy-MM-dd}-{projectName}-machine-software.csv")),
                CultureInfo.InvariantCulture);
        machineSoftwareWriter.WriteHeader<MachineSoftwareInventory>();
        await machineSoftwareWriter.NextRecordAsync();

        await using var dependenciesWriter =
            new CsvWriter(
                File.CreateText(
                    Path.Combine(_options.OutputPath,
                        $"{DateTime.Now:yyyy-MM-dd}-{projectName}-machine-dependencies.csv")),
                CultureInfo.InvariantCulture);
        dependenciesWriter.WriteHeader<DependencyOverTime>();
        await dependenciesWriter.NextRecordAsync();

        var machines = new Dictionary<string, Machine>();

        foreach (var appliance in masterSiteInfo["properties"]!["sites"]!)
        {
            var applianceName = appliance.Value<string>();

            await foreach (var machine in FetchWithNextLink(
                               $"{Root}{applianceName}/machines?{ApiVersion}"))
            {
                _logger.LogInformation($"Processing Machine {machine["properties"]!.Value<string>("displayName")}");

                var machineObj = Machine.FromJToken(machine);

                machines.Add(machineObj.Id.ToLowerInvariant(), machineObj);

                var apps = await FetchApplicationsAndFeaturesForMachine(machine);
                foreach (var app in apps["properties"]!["appsAndRoles"]!["applications"]!)
                {
                    BuildApplicationObject(machineObj, app);
                }

                foreach (var app in apps["properties"]!["appsAndRoles"]!["features"]!)
                {
                    machineObj.Features.Add(app.Value<string>("name")!);
                }

                await machineSoftwareWriter.WriteRecordsAsync(
                    machineObj.Applications
                        .Select(x => MachineSoftwareInventory.FromMachineAndApplication(machineObj, x))
                        .Concat(machineObj.Features.Select(x => MachineSoftwareInventory.FromMachineAndFeature(machineObj, x))));
            }

            _logger.LogInformation($"Fetching Dependencies");
            var (status, dependencies) = await FetchDependencyData(applianceName);

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

                    var dependency = record.ToDependencyOverTime();
                    grouped.Add(dependency);
                }

                await dependenciesWriter.WriteRecordsAsync(grouped);
                await dependenciesWriter.FlushAsync();
            }
        }

        await machineWriter.WriteRecordsAsync(machines.Values.Select(MachineExport.FromMachine));
        await machineWriter.FlushAsync();
        
        return machines;
    }

    private async Task<(string status, JObject dependencies)> FetchDependencyData(string? applianceName)
    {
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

        return (status, dependencies);
    }

    private static void BuildApplicationObject(Machine machineObj, JToken app)
    {
        machineObj.Applications.Add(new Application()
        {
            Name = app.Value<string>("name")!,
            Provider = app.Value<string>("provider")!,
            Version = app.Value<string>("version")!
        });
    }

    private async Task<JObject> FetchApplicationsAndFeaturesForMachine(JToken machine)
    {
        return await _httpClient.GetJsonAsync<JObject>(
            $"{Root}{machine.Value<string>("id")}/applications?{ApiVersion}");
    }


    private async Task ExtractWebAppsAndSqlDatabases(Dictionary<string, Machine> machines, JObject masterSiteInfo, string projectName)
    {
        await using var machineWebsiteWriter =
            new CsvWriter(
                File.CreateText(Path.Combine(_options.OutputPath,
                    $"{DateTime.Now:yyyy-MM-dd}-{projectName}-{projectName}-machines-websites.csv")),
                CultureInfo.InvariantCulture);
        
        machineWebsiteWriter.WriteHeader<MachineWebSiteInventory>();
        await machineWebsiteWriter.NextRecordAsync();

        await using var machineDatabaseWriter =
            new CsvWriter(
                File.CreateText(Path.Combine(_options.OutputPath,
                    $"{DateTime.Now:yyyy-MM-dd}-{projectName}-{projectName}-machines-databases.csv")),
                CultureInfo.InvariantCulture);
        machineDatabaseWriter.WriteHeader<MachineDatabaseInventory>();
        await machineDatabaseWriter.NextRecordAsync();

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
                    sqlServers[sqlServer.Value<string>("id")!] = SqlServer.FromJToken(sqlServer);
                }

                await foreach (var database in FetchWithNextLink(
                                   $"{Root}{nestedSites.Value<string>()}/sqlDatabases?{ApiVersion}"))
                {
                    _logger.LogInformation($"Processing Sql Database {projectName}");
                    var sqlServer = sqlServers[database["properties"]!.Value<string>("sqlServerArmId")!];
                    machineDatabaseWriter.WriteRecord(MachineDatabaseInventory.FromServerAndDatabase(sqlServer, database));
                }
            }
            else if (nestedSite.Value<string>("type") == "Microsoft.OffAzure/MasterSites/WebAppSites")
            {
                _logger.LogInformation($"Processing Web App Sites {projectName}");

                await foreach (var webApplication in FetchWithNextLink(
                                   $"{Root}{nestedSites.Value<string>()}/WebApplications?{ApiVersion}"))
                {
                    var machineId = machines.TryGetValue(
                        webApplication["properties"]!["machineArmIds"]?.FirstOrDefault()?.Value<string>() ?? string.Empty,
                        out var machine)
                        ? machine.Name
                        : "unknown";

                    machineWebsiteWriter.WriteRecord(MachineWebSiteInventory.FromJToken(machineId, webApplication));
                }
            }
        }
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