using Microsoft.Extensions.Configuration;
using Obsidian.API.Configuration;
using Obsidian.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

/// <summary>The whitelist default only applies to fresh installs, so upgrading never locks players out.</summary>
public sealed class ConfigFilesMigration : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"obsidian-config-{Guid.NewGuid():N}");

    [Fact]
    public async Task FreshInstallsWriteTheWhitelistOn()
    {
        var notices = new List<string>();

        await ConfigFiles.GenerateAsync(this.directory, notices.Add);

        Assert.Empty(notices);
        var text = await File.ReadAllTextAsync(Path.Combine(this.directory, "server.json"));
        Assert.Contains("\"whitelist\": true", text);
        Assert.True(this.Load().Whitelist);
    }

    [Fact]
    public async Task ExistingConfigsKeepTheirWhitelist()
    {
        (string Json, bool Whitelist, bool Noticed)[] cases =
        [
            ("{ \"motd\": \"Old server\" }", false, true),
            ("{ \"Whitelist\": true }", true, false),
            ("{ \"whitelist\": false }", false, false)
        ];

        await FixtureCases.AssertAllAsync(
            cases,
            row => $"json={row.Json}, whitelist={row.Whitelist}, noticed={row.Noticed}",
            row => AssertExistingConfigsKeepTheirWhitelist(row.Json, row.Whitelist, row.Noticed));
    }

    private async Task AssertExistingConfigsKeepTheirWhitelist(string json, bool whitelist, bool noticed)
    {
        Directory.CreateDirectory(this.directory);
        var path = Path.Combine(this.directory, "server.json");
        await File.WriteAllTextAsync(path, json);
        var notices = new List<string>();

        await ConfigFiles.GenerateAsync(this.directory, notices.Add);

        Assert.Equal(json, await File.ReadAllTextAsync(path));
        Assert.Equal(noticed ? 1 : 0, notices.Count);
        Assert.Equal(whitelist, this.Load().Whitelist);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.directory))
            Directory.Delete(this.directory, recursive: true);
    }

    private ServerConfiguration Load()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(this.directory, "server.json"))
            .Build();

        return configuration.Get<ServerConfiguration>()!;
    }
}
