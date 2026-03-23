using Microsoft.Extensions.Logging;
using Obsidian.API.Configuration;
using Obsidian.API.Registry.Codecs.Dimensions;
using System.IO;

namespace Obsidian.WorldData;

internal sealed class Dimension(ILogger logger, IPacketBroadcaster packetBroadcaster, ServerConfiguration configuration,
    IEventDispatcher eventDispatcher, ILevelGenerator worldGenerator, string name, IWorld world) :
    AbstractLevel(logger, packetBroadcaster, configuration, eventDispatcher, worldGenerator, name, world.Seed), IDimension
{
    public IWorld ParentWorld { get; } = world;

    public override void Initialize(DimensionCodec codec)
    {
        this.FolderPath = Path.Combine("worlds", ParentWorld.Name, "dimensions", this.Name);

        this.DimensionName = codec.Name;

        this.LevelData = new LevelData
        {
            Time = codec.Element.FixedTime ?? 0,
            DefaultGamemode = Gamemode.Survival,
            GeneratorName = Generator.Id
        };

        this.LevelDataFilePath = Path.Combine(this.FolderPath, "level.dat");

        Directory.CreateDirectory(this.FolderPath);
    }

    public override Task<bool> LoadAsync(DimensionCodec codec) => Task.FromResult(false);
    public override Task SaveAsync() => Task.CompletedTask;
}
