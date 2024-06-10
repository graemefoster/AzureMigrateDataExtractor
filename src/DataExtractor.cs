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
        var api =
            $"{Root}/subscriptions/{_options.SubscriptionId}/resourceGroups/{_options.ResourceGroupName}/providers/Microsoft.Migrate/migrateProjects/{_options.AzureMigrateProjectName}/solutions?{EarlierApiVersion}";

        await foreach (var project in FetchWithNextLink(api))
        {
            if (project["properties"]!["summary"]!.Value<int>("discoveredCount") == 0) continue;

            var masterSiteInfo = await FetchMasterSite(project);
            var projectName = project.Value<string>("name")!;
            _logger.LogInformation($"Processing Project {projectName}");
            var softwareExtract = ExtractSoftware(masterSiteInfo, projectName);
            var webAppSitesExtract = ExtractWebAppSites(masterSiteInfo, projectName);
            var sqlSitesExtract = ExtractSqlSites(masterSiteInfo, projectName);
            var dependenciesExtract = ExtractDependencies(masterSiteInfo, projectName);
            
            await Task.WhenAll(softwareExtract, webAppSitesExtract, sqlSitesExtract, dependenciesExtract);
        }

        _logger.LogInformation($"Finished extracting data. Output files are in {_options.OutputPath}");
    }

    private async Task<JObject> FetchMasterSite(JToken project)
    {
        return await _httpClient.GetJsonAsync<JObject>(
            $"{Root}{project["properties"]!["details"]!["extendedDetails"]!.Value<string>("masterSiteId")}?{ApiVersion}");
    }

    private async Task ExtractSoftware(JObject masterSiteInfo, string projectName)
    {
        await using var machineSoftwareWriter = File.Create(
            Path.Combine(_options.OutputPath,
                $"{DateTime.Now:yyyy-MM-dd}-{projectName}-machine-software.xlsx"));

        _logger.LogInformation($"Fetching Software");

        foreach (var serversites in masterSiteInfo["properties"]!["sites"]!)
        {
            var serverSiteId = serversites.Value<string>();
            var (status, dependencies) = await FetchExtract(serverSiteId, "exportApplications", new { });
            if (status == "Succeeded")
            {
                var resultJson = JsonConvert.DeserializeObject<JObject>(dependencies["properties"]!.Value<string>("result")!)!;
                var extractStream = await new HttpClient().GetStreamAsync(resultJson.Value<string>("SASUri")!);
                await extractStream.CopyToAsync(machineSoftwareWriter);
            }
        }
    }

    private async Task ExtractWebAppSites(JObject masterSiteInfo, string projectName)
    {
        await using var machineWebsiteWriter = File.Create(Path.Combine(_options.OutputPath,
            $"{DateTime.Now:yyyy-MM-dd}-{projectName}-{projectName}-machines-websites.csv"));

        _logger.LogInformation($"Fetching Software");

        foreach (var nestedSites in masterSiteInfo["properties"]!["nestedSites"]!)
        {
            var nestedSite =
                await _httpClient.GetJsonAsync<JObject>($"{Root}{nestedSites.Value<string>()}?{ApiVersion}");

            if (nestedSite.Value<string>("type") == "Microsoft.OffAzure/MasterSites/WebAppSites")
            {
                _logger.LogInformation($"Processing Web App Sites {projectName}");
                var webAppSiteId = nestedSite.Value<string>("id");
                var (status, dependencies) = await FetchExtract(webAppSiteId, "exportInventory", new { });
                if (status == "Succeeded")
                {
                    var resultJson = JsonConvert.DeserializeObject<JObject>(dependencies["properties"]!.Value<string>("result")!)!;
                    var extractStream = await new HttpClient().GetStreamAsync(resultJson.Value<string>("SASUri")!);
                    await extractStream.CopyToAsync(machineWebsiteWriter);
                }
            }
        }
    }


    private async Task ExtractSqlSites(JObject masterSiteInfo, string projectName)
    {
        await using var machineWebsiteWriter = File.Create(Path.Combine(_options.OutputPath,
            $"{DateTime.Now:yyyy-MM-dd}-{projectName}-{projectName}-machines-sqlsites.csv"));

        _logger.LogInformation($"Fetching Software");

        foreach (var nestedSites in masterSiteInfo["properties"]!["nestedSites"]!)
        {
            var nestedSite =
                await _httpClient.GetJsonAsync<JObject>($"{Root}{nestedSites.Value<string>()}?{ApiVersion}");

            if (nestedSite.Value<string>("type") == "Microsoft.OffAzure/MasterSites/SqlSites")
            {
                _logger.LogInformation($"Processing Web App Sites {projectName}");
                var sqlSiteId = nestedSite.Value<string>("id");
                var (status, dependencies) = await FetchExtract(sqlSiteId, "exportSqlServers", new { });
                if (status == "Succeeded")
                {
                    var resultJson = JsonConvert.DeserializeObject<JObject>(dependencies["properties"]!.Value<string>("result")!)!;
                    var extractStream = await new HttpClient().GetStreamAsync(resultJson.Value<string>("SASUri")!);
                    await extractStream.CopyToAsync(machineWebsiteWriter);
                }
            }
        }
    }

    private async Task ExtractDependencies(JObject masterSiteInfo, string projectName)
    {
        await using var dependenciesWriter =
            new CsvWriter(
                File.CreateText(
                    Path.Combine(_options.OutputPath,
                        $"{DateTime.Now:yyyy-MM-dd}-{projectName}-machine-dependencies.csv")),
                CultureInfo.InvariantCulture);
        dependenciesWriter.WriteHeader<DependencyOverTime>();
        await dependenciesWriter.NextRecordAsync();

        foreach (var appliance in masterSiteInfo["properties"]!["sites"]!)
        {
            var applianceName = appliance.Value<string>();

            _logger.LogInformation($"Fetching Dependencies");
            var (status, dependencies) = await FetchExtract(applianceName, "exportDependencies", new
            {
                endTime = DateTimeOffset.UtcNow,
                startTime = DateTimeOffset.UtcNow.AddDays(-14)
            });

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
    }

    private async Task<(string status, JObject resultJson)> FetchExtract(string? provider, string urlPath,
        object postParameters)
    {
        var url = $"{Root}{provider}/{urlPath}?{ApiVersion}";
        _logger.LogInformation("Fetching extract from {Url}", url);
        var dependenciesResponse =
            await _httpClient.PostJsonAsync(url, postParameters);

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