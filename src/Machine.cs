﻿using System.Collections.Generic;
using Newtonsoft.Json.Linq;

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

    public static Machine FromJToken(JToken machine)
    {
        return new Machine()
        {
            Id = machine.Value<string>("id")!,
            Name = machine.Value<string>("name")!,
            DisplayName = machine["properties"]!.Value<string>("displayName")!,
            PowerStatus = machine["properties"]!.Value<string>("powerStatus")!,
            IpAddresses = machine["properties"]!["networkAdapters"]!
                .SelectMany(x => x["ipAddressList"]!.Values<string>().Select(ip => ip!)).ToList()
        };
    }
}