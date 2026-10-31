using Obsidian.API.Containers;
using Obsidian.Entities;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // Vanilla ContainerOpenersCounter schedules an opener recheck every five ticks while a container is open.
    internal const int ContainerLidRecheckDelay = 5;

    private readonly ConcurrentDictionary<Vector, byte> changedContainerLids = [];
    private readonly Dictionary<Vector, (int Block, int Count, long RecheckAt)> openContainerLids = [];
    private long nextContainerLidRecheck;

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
        foreach (var (_, viewer) in this.Players)
        {
            if (!viewer.Alive || viewer is PlayerBase { Connected: false }
                || viewer.GameMode == GameMode.Spectator || viewer.Level != this)
                continue;

            var window = viewer is PlayerBase player
                ? player.ContainerWindow : (viewer.OpenedContainer, this);
            if (window.Item2 != this || window.Item1?.ShowsRetired == true)
                continue;

            foreach (var position in ContainerLidPositions(window.Item1).Distinct())
            {
                yield return position;
            }
        }
    }

    // Runs on the simulation path immediately before the block-event drain. Connections only mark dirty positions;
    // they never compute or enqueue counts, so an older connection cannot publish a stale count after a newer one.
    private void ReconcileContainerLids()
    {
        var now = this.LevelData.GameTime;
        if (this.changedContainerLids.IsEmpty && now < this.nextContainerLidRecheck
            && !this.openContainerLids.Values.Any(opened => now >= opened.RecheckAt))
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

        var counts = new Dictionary<Vector, int>();
        foreach (var position in this.ViewedContainerPositions())
        {
            counts[position] = counts.GetValueOrDefault(position) + 1;
        }

        foreach (var (position, opened) in this.openContainerLids)
        {
            if (now >= opened.RecheckAt)
            {
                changed.Add(position);
            }
        }

        // Discover windows from custom IPlayer implementations that do not use PlayerBase's setter.
        if (now >= this.nextContainerLidRecheck)
        {
            changed.UnionWith(counts.Keys.Where(position => !this.openContainerLids.ContainsKey(position)));
            this.nextContainerLidRecheck = now + ContainerLidRecheckDelay;
        }

        foreach (var position in changed)
        {
            if (!this.IsBlockTicking(position))
            {
                this.changedContainerLids.TryAdd(position, 0);
                continue;
            }

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
