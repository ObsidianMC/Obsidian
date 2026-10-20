using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// The knot of leads tied to a fence, like vanilla's <c>LeashFenceKnotEntity</c>. It holds the leads of the mobs tied
/// to it, and goes when its fence does, or when a player hits it or unties it.
/// </summary>
/// <remarks>
/// Like vanilla, knots aren't saved: a mob tied to one saves the fence's position, and ties itself to a knot there
/// again when it's loaded.
/// </remarks>
[MinecraftEntity("minecraft:leash_knot")]
public sealed partial class LeashKnot : Entity, IPlayerInteractable
{
    // How often the knot checks its fence is still there, in ticks.
    private const int FenceCheckInterval = 20;

    // How far a player's mobs can be to be tied to a fence they use, vanilla's LeadItem range.
    internal const double TieRange = 7;

    private int checkTicks;

    public LeashKnot() => this.Type = EntityType.LeashKnot;

    /// <summary>Whether the knot was removed, which the leads tied to it notice.</summary>
    internal bool Removed { get; private set; }

    /// <summary>The fence the knot is tied to.</summary>
    public Vector Fence => (Vector)this.Position.Floor();

    /// <summary>Whether a block holds leash knots.</summary>
    internal static bool IsFence(IBlock? block) => block is not null && TagsRegistry.Block.Fences.Entries.Contains(block.RegistryId);

    /// <summary>
    /// The knot on <paramref name="fence"/>, made if there's none yet, like vanilla's <c>getOrCreateKnot</c>.
    /// </summary>
    internal static LeashKnot? GetOrCreate(AbstractLevel level, Vector fence)
    {
        var existing = level.GetNonPlayerEntitiesInRange((VectorD)fence + new VectorD(0.5, 0.375, 0.5), 0.5f)
            .OfType<LeashKnot>()
            .FirstOrDefault(knot => !knot.Removed && knot.Fence == fence);
        if (existing is not null)
            return existing;

        var knot = new LeashKnot
        {
            Level = level,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)fence + new VectorD(0.5, 0.375, 0.5)
        };
        knot.LastPosition = knot.Position;

        return level.TrySpawnEntity(knot, hostOwned: true) ? knot : null;
    }

    /// <summary>
    /// Ties the mobs <paramref name="player"/> leads to <paramref name="fence"/>, as vanilla's
    /// <c>LeadItem.bindPlayerMobs</c> does.
    /// </summary>
    /// <returns>Whether any mob was tied.</returns>
    internal static bool TiePlayerMobs(AbstractLevel level, IPlayer player, Vector fence)
    {
        var mobs = LedBy(level, player, TieRange).ToArray();
        if (mobs.Length == 0 || GetOrCreate(level, fence) is not LeashKnot knot)
            return false;

        foreach (var mob in mobs)
            mob.LeashTo(knot);

        knot.PlaySound("minecraft:entity.leash_knot.place");
        return true;
    }

    /// <summary>The mobs within <paramref name="range"/> whose leads <paramref name="holder"/> holds.</summary>
    internal static IEnumerable<Mob> LedBy(ILevel level, IEntity holder, double range) =>
        level.GetNonPlayerEntitiesInRange(holder.Position, (float)range)
            .OfType<Mob>()
            .Where(mob => ReferenceEquals(mob.LeashHolder, holder));

    public override async ValueTask TickAsync()
    {
        if (this.Removed || ++this.checkTicks < FenceCheckInterval)
            return;

        this.checkTicks = 0;
        if (!IsFence(await this.Level.GetBlockAsync(this.Fence)))
            await this.UntieAsync(dropLeads: true);
    }

    /// <summary>
    /// Using the knot ties the player's mobs to it, or, when they lead none, unties the knot, as vanilla's does.
    /// </summary>
    public async ValueTask<bool> InteractAsync(PlayerBase player, InteractionHand hand)
    {
        if (this.Removed || player.GameMode == GameMode.Spectator)
            return false;

        var mobs = LedBy(this.Level, player, TieRange).ToArray();
        if (mobs.Length > 0)
        {
            foreach (var mob in mobs)
                mob.LeashTo(this);

            this.PlaySound("minecraft:entity.leash_knot.place");
            return true;
        }

        await this.UntieAsync(dropLeads: player.GameMode != GameMode.Creative);
        return true;
    }

    /// <summary>Hitting the knot unties it.</summary>
    public ValueTask AttackedAsync(PlayerBase player) => this.UntieAsync(dropLeads: player.GameMode != GameMode.Creative);

    /// <summary>
    /// Removes the knot, taking the leads of the mobs tied to it off, which drop as items with
    /// <paramref name="dropLeads"/>.
    /// </summary>
    internal async ValueTask UntieAsync(bool dropLeads)
    {
        if (this.Removed)
            return;

        this.Removed = true;
        this.PlaySound("minecraft:entity.leash_knot.break");

        foreach (var mob in LedBy(this.Level, this, 12).ToArray())
            mob.DropLeash(dropLeads && this.Level.LevelData.GetBooleanRule("entity_drops"));

        await this.RemoveAsync();
    }

    private void PlaySound(string sound) =>
        this.PacketBroadcaster.QueuePacketToLevelInRange(this.Level, this.Position, new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(this.Position.X, this.Position.Y, this.Position.Z),
            Category = SoundCategory.Neutral,
            Volume = 1,
            Pitch = 1,
            Seed = Random.Shared.NextInt64()
        });
}
