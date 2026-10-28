using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Checks that the version constants in <see cref="ServerConstants"/> match the vanilla version Obsidian's assets were
/// generated from, so a version bump can't update one and miss the others.
/// </summary>
public class VanillaVersion
{
    [Fact]
    public void ConstantsMatchTheServerJar()
    {
        // The server jar's version.json, which the asset generator copies into the assets.
        using var stream = typeof(ServerConstants).Assembly.GetManifestResourceStream("Obsidian.Assets.version.json")!;
        using var json = JsonDocument.Parse(stream);
        var version = json.RootElement;
        var packVersion = version.GetProperty("pack_version");

        Assert.Equal(ServerConstants.ProtocolDescription, version.GetProperty("id").GetString());
        Assert.Equal((int)ServerConstants.DefaultProtocol, version.GetProperty("protocol_version").GetInt32());
        Assert.Equal(ServerConstants.DataVersion, version.GetProperty("world_version").GetInt32());
        Assert.Equal(ServerConstants.DataPackFormat,
            (packVersion.GetProperty("data_major").GetInt32(), packVersion.GetProperty("data_minor").GetInt32()));
    }
}
