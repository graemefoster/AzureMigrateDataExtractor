# Azure Migrate Data Extractor

## Purpose
A tool to extract data in CSV format from an Azure Migrate project. This tool does not send the data anywhere. It collects it in CSV files on the machine you run it from.

## Downloading

Either build from source or download the latest executable from the [releases](https://github.com/graemefoster/AzureMigrateDataExtractor/releases). 

> Releases are currently built for Windows x64 only. Prerequisite of dotnet 8 runtime is required for the exe to run.

## Running

```shell
.\AzureMigrateDataExtractor.exe --tenant-id "<tenant-id>" --subscription-id "<subscription-id>" --resource-group-name "<resource-group-name>" --azure-migrate-project-name "<project-name>" --output-path "<output-path>"
cd <output-path>
dir *.csv
```

## Output

The tool will generate the following CSV files:

| File             | Contents                                                  |
|------------------|-----------------------------------------------------------|
| dependencies.csv | Contains the dependencies between the discovered servers. |
| databases.csv    | Contains discovered sql server / databases                |
| software.csv     | Contains discovered applications and features             |
| websites.csv     | Contains discovered websites                              |



