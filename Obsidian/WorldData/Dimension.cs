using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using Obsidian.API.Registry.Codecs.Dimensions;
using System.IO;

namespace Obsidian.WorldData;

internal sealed class Dimension(ILogger<Dimension> logger, IPacketBroadcaster packetBroadcaster, IOptionsMonitor<ServerConfiguration> configuration,
    IEventDispatcher eventDispatcher, ILevelGenerator worldGenerator, string name, IWorld world) :
    AbstractLevel(logger, packetBroadcaster, configuration, eventDispatcher, worldGenerator, name, world.Seed), IDimension
{
    public IWorld ParentWorld { get; } = world;

    private bool UsesVanillaLayout => this.ParentWorld is World { UsesVanillaLayout: true };

    protected override string RegionFolderName => this.UsesVanillaLayout ? "region" : "regions";

    public override void Initialize(DimensionCodec codec)
    {
        this.FolderPath = this.UsesVanillaLayout
            ? Path.Combine(this.ParentWorld.FolderPath, VanillaFolder(this.Name))
            : Path.Combine(ServerConstants.WorldsPath, ParentWorld.Name, "dimensions", this.Name.TrimResourceTag(true));

        this.SetDimension(codec);

        this.LevelData = new LevelData
        {
            Time = codec.Element.FixedTime ?? 0,
            DefaultGamemode = GameMode.Survival,
            GeneratorName = Generator.Id
        };

        this.LevelDataFilePath = Path.Combine(this.FolderPath, "level.dat");

        Directory.CreateDirectory(this.FolderPath);
    }

    /// <summary>
    /// A dimension's folder in a vanilla save (vanilla's <c>DimensionType.getStorageFolder</c>): <c>DIM-1</c> for the
    /// nether, <c>DIM1</c> for the end, and <c>dimensions/&lt;namespace&gt;/&lt;path&gt;</c> for others.
    /// </summary>
    internal static string VanillaFolder(string dimension)
    {
        switch (dimension)
        {
            case "minecraft:the_nether":
                return "DIM-1";
            case "minecraft:the_end":
                return "DIM1";
        }

        var id = dimension.Contains(':') ? dimension : $"minecraft:{dimension}";
        var parts = id.Split(':', 2);

        return Path.Combine("dimensions", parts[0], parts[1]);
    }

    public override Task<bool> LoadAsync(DimensionCodec codec) => Task.FromResult(false);
    public override Task SaveAsync() => Task.CompletedTask;
}
