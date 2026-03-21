using Microsoft.Extensions.Logging;
using Obsidian.API.Configuration;

namespace Obsidian.WorldData;

internal sealed class Dimension(IWorld world, ILogger logger, IPacketBroadcaster packetBroadcaster, ServerConfiguration configuration,
    IEventDispatcher eventDispatcher, ILevelGenerator worldGenerator, string name) :
    AbstractLevel(logger, packetBroadcaster, configuration, eventDispatcher, worldGenerator, name, world.Seed), IDimension
{
    public IWorld ParentWorld { get; } = world;
}
