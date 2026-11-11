using Obsidian.API.Containers;
using Obsidian.API.BlockEntities;
using Obsidian.Entities;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // Vanilla ContainerOpenersCounter schedules an opener recheck every five ticks while a container is open.
    internal const int ContainerLidRecheckDelay = 5;

    // A window opening or closing at a container, and the game time it happened at: vanilla schedules a container's
    // first opener recheck from the moment it opens.
    private readonly record struct ContainerLidChange(IPlayer? ClosingPlayer, long ChangedAt);

    private readonly ConcurrentDictionary<Vector, ContainerLidChange> changedContainerLids = [];
    private readonly Dictionary<Vector, (int Block, int Count, long RecheckAt)> openContainerLids = [];
    private readonly PriorityQueue<Vector, long> containerLidDeadlines = new();
    private readonly HashSet<Vector> inactiveContainerLids = [];

    private long nextInactiveContainerLidCheck = long.MaxValue;

    internal long ContainerLidViewerScans { get; private set; }

    internal bool IsContainerWindowValid(BaseContainer container)
    {
        if (container is not Container { Id: "ender_chest" } ender)
            return true;

        BlockEntity? owner;
        if (container.WindowBlockEntity is { } binding)
        {
            if (!binding.TryGetTarget(out owner))
                return false;
        }
        else
        {
            owner = this.GetResidentBlockEntity(ender.BlockPosition) as BlockEntity;
            if (owner?.Id != "minecraft:ender_chest")
                return false;

            container.WindowBlockEntity = new(owner);
        }

        return !owner.IsRemoved && ReferenceEquals(owner, this.GetResidentBlockEntity(ender.BlockPosition));
    }

    internal void ContainerWindowChanged(BaseContainer? container, IPlayer? closingPlayer = null)
    {
        var now = this.LevelData?.GameTime ?? 0;
        foreach (var position in ContainerLidPositions(container))
        {
            if (closingPlayer is null)
                this.changedContainerLids.TryAdd(position, new(null, now));
            else
                this.changedContainerLids[position] = new(closingPlayer, now);
        }
    }

    private static IEnumerable<Vector> ContainerLidPositions(BaseContainer? container)
    {
        if (container is DoubleChestContainer chest)
        {
            foreach (var position in ContainerLidPositions(chest.First).Concat(ContainerLidPositions(chest.Second)))
            {
                yield return position;
            }
        }
        else if (container is Container physical
            && (physical.BlockEntityOwner is not null || physical.Id is "chest" or "ender_chest" or "shulker_box"))
        {
            yield return physical.BlockPosition;
        }
    }

    private IEnumerable<(Vector Position, IPlayer Viewer)> ViewedContainerPositions()
    {
        this.ContainerLidViewerScans++;
        foreach (var (_, viewer) in this.Players)
        {
            if (!viewer.Alive || viewer is PlayerBase { Connected: false }
                || viewer.GameMode == GameMode.Spectator || viewer.Level != this)
                continue;

            var window = viewer is PlayerBase player
                ? player.ContainerWindow : (viewer.OpenedContainer, this);
            if (window.Item2 != this)
                continue;

            if (window.Item1 is not null
                && (window.Item1.ShowsRetired || !this.IsContainerWindowValid(window.Item1)))
            {
                if (viewer is PlayerBase invalid)
                {
                    invalid.CloseInvalidContainerWindow(window.Item1);
                }

                continue;
            }

            foreach (var position in ContainerLidPositions(window.Item1).Distinct())
            {
                if (!this.inactiveContainerLids.Contains(position))
                {
                    yield return (position, viewer);
                }
            }
        }
    }

    // Runs on the simulation path immediately before the block-event drain. Connections only mark dirty positions;
    // they never compute or enqueue counts, so an older connection cannot publish a stale count after a newer one.
    private void ReconcileContainerLids()
    {
        var now = this.LevelData.GameTime;
        var checkInactive = this.inactiveContainerLids.Count > 0 && now >= this.nextInactiveContainerLidCheck;
        if (this.changedContainerLids.IsEmpty && !checkInactive
            && (!this.containerLidDeadlines.TryPeek(out _, out var deadline) || now < deadline))
        {
            return;
        }

        var changed = new HashSet<Vector>();
        var closingPlayers = new Dictionary<Vector, IPlayer?>();
        var changedAt = new Dictionary<Vector, long>();
        foreach (var (position, _) in this.changedContainerLids)
        {
            if (this.changedContainerLids.TryRemove(position, out var change))
            {
                changed.Add(position);
                closingPlayers[position] = change.ClosingPlayer;
                changedAt[position] = change.ChangedAt;
            }
        }

        if (checkInactive)
        {
            foreach (var position in this.inactiveContainerLids.ToArray())
            {
                if (this.IsBlockTicking(position))
                {
                    this.inactiveContainerLids.Remove(position);
                    changed.Add(position);
                }
            }

            this.nextInactiveContainerLidCheck = now + ContainerLidRecheckDelay;
        }

        while (this.containerLidDeadlines.TryPeek(out var position, out var due) && now >= due)
        {
            this.containerLidDeadlines.Dequeue();
            if (this.openContainerLids.TryGetValue(position, out var opened) && opened.RecheckAt == due)
            {
                changed.Add(position);
            }
        }

        if (changed.Count == 0)
            return;

        foreach (var position in changed)
        {
            this.inactiveContainerLids.Remove(position);
        }

        var counts = new Dictionary<Vector, int>();
        var firstViewers = new Dictionary<Vector, IPlayer>();
        foreach (var (position, viewer) in this.ViewedContainerPositions())
        {
            counts[position] = counts.GetValueOrDefault(position) + 1;
            firstViewers.TryAdd(position, viewer);
        }

        // A double chest's compound container visits the right half first, as its slot order does.
        foreach (var position in changed.OrderBy(position =>
            this.GameEventBlock(position)?.GetProperty("type") == "right" ? 0 : 1))
        {
            if (!this.IsBlockTicking(position))
            {
                this.openContainerLids.Remove(position);
                this.inactiveContainerLids.Add(position);
                this.nextInactiveContainerLidCheck = Math.Min(this.nextInactiveContainerLidCheck,
                    now + ContainerLidRecheckDelay);
                continue;
            }

            this.inactiveContainerLids.Remove(position);
            var block = this.GameEventBlock(position);
            if (block is null || !HasContainerOpeners(block))
            {
                this.openContainerLids.Remove(position);
                continue;
            }

            var count = counts.GetValueOrDefault(position);
            var previous = this.openContainerLids.GetValueOrDefault(position);
            var wasOpen = previous.Block == block.RegistryId && previous.Count > 0;
            if (wasOpen != (count > 0))
            {
                var source = count > 0
                    ? firstViewers.GetValueOrDefault(position) : closingPlayers.GetValueOrDefault(position);
                this.ContainerOpenedOrClosed(position, block, count > 0, source);
            }

            var shulker = IsShulkerBox(block);
            if (block.RegistryId != BlockIds.Barrel)
            {
                // A later lifecycle change can coalesce a pending count key. Read the payload at dispatch too,
                // so an older queued request can never close a lid that has acquired a new viewer meanwhile.
                // A shulker box's lid moves on the server too, from the count the event carries.
                this.QueueBlockEvent(position, block.RegistryId, 1, count,
                    trigger: shulker ? () => this.MoveShulkerLid(position) : null,
                    parameterAtDispatch: () => this.ViewerCount(position));
            }

            // Vanilla's shulker boxes count their viewers themselves and never recheck them.
            if (count > 0 && shulker)
            {
                this.openContainerLids[position] = (block.RegistryId, count, long.MaxValue);
            }
            else if (count > 0)
            {
                // The first recheck is due five ticks after the container opened, as vanilla schedules it then.
                var openedAt = changedAt.TryGetValue(position, out var at) ? at : now;
                var next = wasOpen && previous.RecheckAt > now
                    ? previous.RecheckAt : (wasOpen ? now : openedAt) + ContainerLidRecheckDelay;
                this.openContainerLids[position] = (block.RegistryId, count, next);
                if (!wasOpen || previous.RecheckAt != next)
                {
                    this.containerLidDeadlines.Enqueue(position, next);
                }
            }
            else
            {
                this.openContainerLids.Remove(position);
            }
        }
    }

    // Vanilla's ContainerOpenersCounter onOpen and onClose with their game events: the lid's sound, a barrel's open
    // state, and container_open from the first viewer or container_close from the final closing player. A periodic
    // recheck has no acting player and emits its close without a source.
    private void ContainerOpenedOrClosed(Vector position, IBlock block, bool opening, IPlayer? viewer)
    {
        this.PlayContainerLidSound(position, block, opening);

        if (block.RegistryId == BlockIds.Barrel)
        {
            lock (this.Fluids.TickLock)
                this.BlockUpdates.SetBlock(position, block.WithProperty("open", opening));
        }

        var center = (VectorD)position + new VectorD(0.5, 0.5, 0.5);
        if (opening)
        {
            this.EmitGameEvent(MobGameEvent.ContainerOpen, center, viewer);
            return;
        }

        this.EmitGameEvent(MobGameEvent.ContainerClose, center, viewer);
    }

    private int ViewerCount(Vector position) =>
        this.ViewedContainerPositions().Count(viewed => viewed.Position == position);

    // The block event's server side for a shulker box: its lid starts opening or closing.
    private ValueTask<bool> MoveShulkerLid(Vector position)
    {
        if (this.GetResidentBlockEntity(position) is ShulkerBoxBlockEntity box)
            box.ViewersChanged(this.ViewerCount(position));

        return ValueTask.FromResult(true);
    }

    private static bool IsShulkerBox(IBlock block) =>
        TagsRegistry.Block.ShulkerBoxes.Entries.Contains(block.RegistryId);

    private static bool HasContainerOpeners(IBlock block) =>
        block.RegistryId is BlockIds.Chest or BlockIds.TrappedChest or BlockIds.EnderChest or BlockIds.Barrel
        || TagsRegistry.Block.CopperChests.Entries.Contains(block.RegistryId)
        || TagsRegistry.Block.ShulkerBoxes.Entries.Contains(block.RegistryId);

    // Vanilla's CopperChestBlock sounds: weathered and oxidized chests have their own.
    private static string CopperChestSound(IBlock block)
    {
        var name = block.UnlocalizedName;
        if (name.Contains("oxidized", StringComparison.Ordinal))
            return "copper_chest_oxidized";

        if (name.Contains("weathered", StringComparison.Ordinal))
            return "copper_chest_weathered";

        return "copper_chest";
    }

    private void PlayContainerLidSound(Vector position, IBlock block, bool opening)
    {
        // Vanilla plays one sound for a double chest, from the right half at the joined lid's center.
        if (block.GetProperty("type") == "left")
            return;

        var sound = block.RegistryId switch
        {
            BlockIds.Chest or BlockIds.TrappedChest => "chest",
            BlockIds.EnderChest => "ender_chest",
            BlockIds.Barrel => "barrel",
            _ when TagsRegistry.Block.CopperChests.Entries.Contains(block.RegistryId) => CopperChestSound(block),
            _ when TagsRegistry.Block.ShulkerBoxes.Entries.Contains(block.RegistryId) => "shulker_box",
            _ => null
        };
        if (sound is null)
            return;

        var action = opening ? "open" : "close";
        var center = (VectorD)position + 0.5;
        if (block.GetProperty("type") == "right")
        {
            var facing = WorldData.Features.FeatureHelpers.ParseFace(block.GetProperty("facing"));
            center += (VectorD)facing.CounterClockWise().ToVector() * 0.5;
        }

        this.PacketBroadcaster.BroadcastToLevelInRange(this, center, new Net.Packets.Play.Clientbound.SoundPacket
        {
            SoundLocation = $"minecraft:block.{sound}.{action}",
            SoundPosition = new SoundPosition(center.X, center.Y, center.Z),
            Category = SoundCategory.Blocks,
            Volume = 0.5f,
            Pitch = Globals.Random.NextSingle() * 0.1f + 0.9f,
            Seed = Globals.Random.NextInt64()
        });
    }
}
