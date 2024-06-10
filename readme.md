# Azure Migrate Data Extractor

## Purpose
A tool to extract data in CSV format from an Azure Migrate project. This tool does not send the data anywhere. It collects it in CSV files on the machine you run it from.

## Downloading

Either build from source, use a ```dotnet global tool``` or download the latest executable from the [releases](https://github.com/graemefoster/AzureMigrateDataExtractor/releases). 

> Releases are currently built for Windows x64 only. Prerequisite of dotnet 8 runtime is required for the exe to run.

## Running

```shell
#Run as a dotnet global tool
dotnet tool install -g AzureMigrateDataExtractor
dotnet AzureMigrateDataExtractor --tenant-id "<tenant-id>" --subscription-id "<subscription-id>" --resource-group-name "<resource-group-name>" --azure-migrate-project-name "<project-name>" --appliance-name "<appliance-name>" --output-path "<output-path>"
cd <output-path>
dir *.csv
```

```powershell
# Run from a win64 single file (needs dotnet 8 runtime installed)

Invoke-WebRequest -Uri 'https://github.com/graemefoster/AzureMigrateDataExtractor/releases/latest/download/AzureMigrateDataExtractor.exe' -OutFile 'c:\temp\AzureMigrateDataExtractor.exe'
cd c:\temp\
.\AzureMigrateDataExtractor.exe --tenant-id "<tenant-id>" --subscription-id "<subscription-id>" --resource-group-name "<resource-group-name>" --azure-migrate-project-name "<project-name>" --appliance-name "<appliance-name>" --output-path "<output-path>"
cd <output-path>
dir *.csv
```

## Output

The tool will generate the following files:

| File         | Format | Contents                                                  |
|--------------|--------|-----------------------------------------------------------|
| dependencies | csv    | Contains the dependencies between the discovered servers. |
| databases    | csv    | Contains discovered sql server / databases                |
| software     | xlsx   | Contains discovered applications and features             |
| websites     | csv    | Contains discovered websites                              |

~~~~