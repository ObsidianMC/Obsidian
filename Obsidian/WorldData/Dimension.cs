using Microsoft.Extensions.Logging;
using Obsidian.API.Configuration;

namespace Obsidian.WorldData;

internal sealed class Dimension : AbstractLevel, IDimension
{
    public IWorld ParentWorld { get; }

    protected override IWorld OwningWorld => this.ParentWorld;

    internal Dimension(IWorld world, ILogger logger, IPacketBroadcaster packetBroadcaster, ServerConfiguration configuration,
        IEventDispatcher eventDispatcher, ILevelGenerator worldGenerator, string name)
        : base(logger, packetBroadcaster, configuration, eventDispatcher, worldGenerator, name, world.Seed)
    {
        this.ParentWorld = world;
    }
}
