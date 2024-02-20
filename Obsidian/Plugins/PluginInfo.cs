﻿using Obsidian.API.Plugins;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Obsidian.Plugins;

public sealed class PluginInfo : IPluginInfo
{
    public required string Name { get; init; }
    public required string Id { get; init; }
    public required Version Version { get; init; } = new Version();

    public PluginDependency[] Dependencies { get; init; } = [];
    public string Description { get; init; } = string.Empty;
    public string[] Authors { get; init; } = [];
    public Uri ProjectUrl { get; init; } = default!;

    [JsonConstructor]
    internal PluginInfo() { }

    [SetsRequiredMembers]
    internal PluginInfo(string name)
    {
        Name = name;
        Id = name;
        Authors = ["Unknown"];
    }
}

public readonly struct PluginDependency
{
    public required string Id { get; init; }

    public required Version Version { get; init; }
}
