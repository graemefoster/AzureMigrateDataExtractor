# Azure Migrate Data Extractor

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



