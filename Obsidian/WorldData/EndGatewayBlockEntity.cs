using System.Threading;
using Obsidian.API.BlockEntities;
using Obsidian.Nbt;

namespace Obsidian.WorldData;

/// <summary>
/// An End gateway's block entity, like vanilla's <c>TheEndGatewayBlockEntity</c>: its age, which clients draw the
/// beam from, the 40-tick cooldown after it sends something through or flashes its beam every 2400 ticks, and where it
/// sends entities (see <see cref="EndGateways"/>).
/// </summary>
internal sealed class EndGatewayBlockEntity : BlockEntity
{
    /// <summary>Vanilla's <c>COOLDOWN_TIME</c>.</summary>
    internal const int CooldownTicks = 40;

    // Vanilla's SPAWN_TIME, while the beam grows in, and ATTENTION_INTERVAL, how often it flashes.
    private const int SpawnTicks = 200;
    private const int AttentionInterval = 2400;

    // Vanilla's Level.isInSpawnableBounds, which a saved exit must lie within.
    private const int SpawnableBound = 30_000_000;
    private const int SpawnableHeight = 20_000_000;

    private NbtCompound others = new();
    private long age;
    private int cooldown;
    private Vector? exitPortal;
    private bool exactTeleport;

    internal override bool SavesOnAnyThread => true;

    /// <summary>The ticks since it was made, which vanilla saves as <c>Age</c>.</summary>
    public long Age => Interlocked.Read(ref this.age);

    /// <summary>Whether it's still cooling down from the last thing it sent or its last flash.</summary>
    public bool CoolingDown => Volatile.Read(ref this.cooldown) > 0;

    /// <summary>The gateway it leads to (vanilla's <c>exit_portal</c>), if it knows one.</summary>
    public Vector? ExitPortal => this.exitPortal;

    /// <summary>
    /// Whether it sends entities to <see cref="ExitPortal"/> itself; otherwise to the top of the tallest block around
    /// it (see <see cref="EndGateways.SafeExit"/>).
    /// </summary>
    public bool ExactTeleport => this.exactTeleport;

    /// <summary>Vanilla's <c>setExitPosition</c>.</summary>
    internal void SetExit(Vector exit, bool exact)
    {
        this.exitPortal = exit;
        this.exactTeleport = exact;
        this.SetChanged();
    }

    protected internal override void Load(NbtCompound tag)
    {
        var others = DataBlockEntity.Copy(tag);
        others.Remove("Age");
        others.Remove("exit_portal");
        others.Remove("ExactTeleport");
        this.others = others;

        this.age = tag.TryGetNumber<long>("Age", out var age) ? age : 0;
        this.exitPortal = tag.TryGetTag<NbtArray<int>>("exit_portal", out var exit) && exit.Count == 3
            ? ReadExit(exit.GetArray())
            : null;
        this.exactTeleport = tag.TryGetBool("ExactTeleport", out var exact) && exact;
    }

    protected internal override void Save(NbtCompound tag)
    {
        foreach (var (name, child) in DataBlockEntity.Copy(Volatile.Read(ref this.others)))
            tag.Add(name, child);

        this.SaveOwn(tag);
    }

    // Vanilla's getUpdateTag, saveCustomOnly: the same fields, which clients draw the beam from.
    protected internal override NbtCompound? GetUpdateData()
    {
        var data = new NbtCompound();
        this.SaveOwn(data);
        return data;
    }

    private void SaveOwn(NbtCompound tag)
    {
        tag.Add(new NbtTag<long>("Age", this.Age));
        if (this.exitPortal is Vector exit)
            tag.Add(new NbtArray<int>("exit_portal", [exit.X, exit.Y, exit.Z]));

        if (this.exactTeleport)
            tag.Add(new NbtTag<bool>("ExactTeleport", true));
    }

    // Vanilla's portalTick: it ages, cools down, and every 2400 ticks it isn't cooling down, flashes its beam. A change
    // of whether it's growing in or cooling down marks it changed.
    protected internal override ValueTask TickAsync()
    {
        if (this.Level is not AbstractLevel level)
            return ValueTask.CompletedTask;

        var spawning = this.Age < SpawnTicks;
        var coolingDown = this.CoolingDown;
        var age = Interlocked.Increment(ref this.age);
        if (coolingDown)
            Interlocked.Decrement(ref this.cooldown);
        else if (age % AttentionInterval == 0)
            this.TriggerCooldown(level, this.BlockPosition);

        if (spawning != this.Age < SpawnTicks || coolingDown != this.CoolingDown)
            this.SetChanged();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Vanilla's <c>triggerCooldown</c>: it cools down for 40 ticks, and its block event, queued at
    /// <paramref name="eventAt"/>, flashes the beam of the gateway there for the players around, its server side
    /// starting that gateway's cooldown again. The event runs only if a gateway is still there.
    /// </summary>
    internal void TriggerCooldown(AbstractLevel level, Vector eventAt)
    {
        Volatile.Write(ref this.cooldown, CooldownTicks);
        level.QueueBlockEvent(eventAt, BlockIds.EndGateway, 1, 0, trigger: () =>
        {
            if (level.GetResidentBlockEntity(eventAt) is not EndGatewayBlockEntity flashing)
                return ValueTask.FromResult(false);

            Volatile.Write(ref flashing.cooldown, CooldownTicks);
            return ValueTask.FromResult(true);
        });
        this.SetChanged();
    }

    /// <summary>
    /// Vanilla's <c>EndGatewayBlock.entityInside</c> for the gateway: true, starting its cooldown, when it isn't
    /// cooling down and sends what touched it. <paramref name="eventAt"/> is where its block event goes (see
    /// <see cref="EndGateways.EventPosition"/>).
    /// </summary>
    internal bool TryUse(AbstractLevel level, Vector eventAt)
    {
        // Players' and other entities' portal ticks may reach it at once; only one starts the cooldown.
        if (Interlocked.CompareExchange(ref this.cooldown, CooldownTicks, 0) != 0)
            return false;

        this.TriggerCooldown(level, eventAt);
        return true;
    }

    private static Vector? ReadExit(int[] values)
    {
        var exit = new Vector(values[0], values[1], values[2]);
        var inside = Math.Abs(exit.X) < SpawnableBound
            && Math.Abs(exit.Z) < SpawnableBound
            && exit.Y >= -SpawnableHeight
            && exit.Y < SpawnableHeight;
        return inside ? exit : null;
    }
}

/// <summary>
/// Where End gateways send entities, like vanilla's <c>TheEndGatewayBlockEntity.getPortalPosition</c>.
/// </summary>
internal static class EndGateways
{
    /// <summary>How far around an exit the safe exit's search reaches.</summary>
    internal const int SafeExitReach = 5;

    // Vanilla's deflation of the box its inside-block walk covers, a float.
    private const double InsideMargin = 1.0E-5F;

    /// <summary>
    /// Where the block event of a gateway that <paramref name="box"/> touches goes. Vanilla's gateway queues it at the
    /// position its inside-block walk hands it, which keeps moving to the walk's last block: the top corner of the
    /// box. Unless that's the gateway itself, as for something no taller than it, the event finds no gateway there:
    /// no beam flashes, and the cooldown isn't started again on the event's tick, so it ends a tick sooner.
    /// </summary>
    internal static Vector EventPosition(BoundingBox box) => new(
        (int)Math.Floor(box.Max.X - InsideMargin),
        (int)Math.Floor(box.Max.Y - InsideMargin),
        (int)Math.Floor(box.Max.Z - InsideMargin));

    /// <summary>
    /// Where an entity sent to <paramref name="exit"/> arrives: the exit itself for an exact gateway, otherwise its
    /// safe exit; at the bottom centre of the block.
    /// </summary>
    internal static VectorD Arrival(AbstractLevel level, Vector exit, bool exact)
    {
        var block = exact ? exit : SafeExit(level, exit);
        return new VectorD(block.X + 0.5, block.Y, block.Z + 0.5);
    }

    /// <summary>
    /// Vanilla's <c>findExitPosition</c>: above the tallest whole block within 5 columns of the exit, leaving out the
    /// exit's own column and bedrock, searched from two above the exit; or three above the exit without one.
    /// </summary>
    internal static Vector SafeExit(AbstractLevel level, Vector exit)
    {
        var around = exit + new Vector(0, 2, 0);
        return TallestBlock(level, around, SafeExitReach, allowBedrock: false) + Vector.Up;
    }

    // Vanilla's findTallestBlock, whose order decides between columns of the same height: x, then z, each column from
    // the top of the world down to the tallest found so far.
    private static Vector TallestBlock(AbstractLevel level, Vector around, int reach, bool allowBedrock)
    {
        Vector? tallest = null;
        var top = level.MinY + level.Height - 1;
        for (var dx = -reach; dx <= reach; dx++)
        {
            for (var dz = -reach; dz <= reach; dz++)
            {
                if (dx == 0 && dz == 0 && !allowBedrock)
                    continue;

                var floor = tallest?.Y ?? level.MinY;
                for (var y = top; y > floor; y--)
                {
                    var position = new Vector(around.X + dx, y, around.Z + dz);
                    var block = level.BlockUpdates.GetBlock(position);
                    if (block.IsCollisionShapeFullBlock() && (allowBedrock || !block.Is(BlockIds.Bedrock)))
                    {
                        tallest = position;
                        break;
                    }
                }
            }
        }

        return tallest ?? around;
    }
}
