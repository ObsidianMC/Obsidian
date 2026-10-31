using Obsidian.API.Containers;
using Obsidian.API.BlockEntities;
using Obsidian.Entities;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // Vanilla ContainerOpenersCounter schedules an opener recheck every five ticks while a container is open.
    internal const int ContainerLidRecheckDelay = 5;

    private readonly ConcurrentDictionary<Vector, byte> changedContainerLids = [];
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

    internal void ContainerWindowChanged(BaseContainer? container)
    {
        foreach (var position in ContainerLidPositions(container))
        {
            this.changedContainerLids.TryAdd(position, 0);
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

    private IEnumerable<Vector> ViewedContainerPositions()
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
                    yield return position;
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
        foreach (var (position, _) in this.changedContainerLids)
        {
            if (this.changedContainerLids.TryRemove(position, out _))
            {
                changed.Add(position);
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
        foreach (var position in this.ViewedContainerPositions())
        {
            counts[position] = counts.GetValueOrDefault(position) + 1;
        }

        foreach (var position in changed)
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
                this.PlayContainerLidSound(position, block, count > 0);
            }

            if (block.RegistryId != BlockIds.Barrel)
            {
                // A later lifecycle change can coalesce a pending count key. Read the payload at dispatch too,
                // so an older queued request can never close a lid that has acquired a new viewer meanwhile.
                this.QueueBlockEvent(position, block.RegistryId, 1, count,
                    parameterAtDispatch: () => this.ViewedContainerPositions().Count(viewed => viewed == position));
            }

            if (count > 0)
            {
                var next = wasOpen && previous.RecheckAt > now
                    ? previous.RecheckAt : now + ContainerLidRecheckDelay;
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

    private static bool HasContainerOpeners(IBlock block) =>
        block.RegistryId is BlockIds.Chest or BlockIds.TrappedChest or BlockIds.EnderChest or BlockIds.Barrel
        || TagsRegistry.Block.CopperChests.Entries.Contains(block.RegistryId)
        || TagsRegistry.Block.ShulkerBoxes.Entries.Contains(block.RegistryId);

    private void PlayContainerLidSound(Vector position, IBlock block, bool opening)
    {
        var sound = block.RegistryId switch
        {
            BlockIds.Chest or BlockIds.TrappedChest => "chest",
            BlockIds.Barrel => "barrel",
            _ when !opening && TagsRegistry.Block.CopperChests.Entries.Contains(block.RegistryId) => "chest",
            _ when TagsRegistry.Block.ShulkerBoxes.Entries.Contains(block.RegistryId) => "shulker_box",
            _ => null
        };
        if (sound is null)
            return;

        var action = opening ? "open" : "close";
        this.BlockUpdates.PlaySound(position, $"minecraft:block.{sound}.{action}",
            0.5f, Globals.Random.NextSingle() * 0.1f + 0.9f);
    }
}
