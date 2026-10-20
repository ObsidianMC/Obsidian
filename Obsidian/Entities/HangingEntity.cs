using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// Something players use and hit that isn't a mob, like an item frame or a leash knot.
/// </summary>
internal interface IPlayerInteractable
{
    /// <summary>A player right-clicks it with the item in <paramref name="hand"/>.</summary>
    /// <returns>Whether it used the interaction.</returns>
    ValueTask<bool> InteractAsync(PlayerBase player, InteractionHand hand);

    /// <summary>A player hits it.</summary>
    ValueTask AttackedAsync(PlayerBase player);
}

/// <summary>
/// An entity hung on a block, like vanilla's <c>HangingEntity</c>: an item frame or a painting. It hangs in the block
/// in front of the one it's on, breaks when that block no longer holds it, and drops its item when it breaks.
/// </summary>
public abstract class HangingEntity : Entity, IPlayerInteractable
{
    // Vanilla checks whether a hanging entity still survives every 100 ticks.
    private const int SurvivalCheckInterval = 100;

    private int checkTicks;
    private bool broken;

    /// <summary>The direction it faces, away from the block it hangs on.</summary>
    public BlockFace Facing { get; set; } = BlockFace.South;

    /// <summary>
    /// The block it's in, in front of the one it hangs on, which its position is worked out from (see
    /// <see cref="Hang"/>). A large painting covers blocks around it too.
    /// </summary>
    /// <remarks>Until it's hung, the block its position is in.</remarks>
    public Vector BlockPosition => this.block ?? (Vector)this.Position.Floor();

    private Vector? block;

    /// <summary>Hangs it in <paramref name="block"/>, facing <paramref name="facing"/>.</summary>
    public void Hang(Vector block, BlockFace facing)
    {
        this.block = block;
        this.Facing = facing;
        this.Position = this.CalculatePosition();
        this.LastPosition = this.Position;
    }

    /// <summary>
    /// Where its position is, like vanilla's <c>calculateBoundingBox</c>: centered 0.46875 from the middle of its
    /// block toward the block it hangs on.
    /// </summary>
    protected virtual VectorD CalculatePosition() =>
        (VectorD)this.BlockPosition + new VectorD(0.5, 0.5, 0.5) - (VectorD)this.Facing.ToVector() * 0.46875;

    /// <summary>The item it drops when it breaks.</summary>
    protected abstract Item DropItem { get; }

    /// <summary>The sound of it breaking, and of it being placed.</summary>
    protected abstract string BreakSound { get; }

    protected abstract string PlaceSound { get; }

    /// <summary>Like vanilla, the spawn packet carries the facing, which the client needs to hang it.</summary>
    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0) => base.SpawnEntity(velocity, FacingId(this.Facing));

    public override async ValueTask TickAsync()
    {
        if (this.broken || ++this.checkTicks < SurvivalCheckInterval)
            return;

        this.checkTicks = 0;
        if (this.Level is AbstractLevel level && !await this.SurvivesAsync(level))
            await this.BreakAsync(dropItems: true);
    }

    /// <summary>
    /// Whether it can hang where it is, like vanilla's <c>survives</c>: every block it covers is free and has a solid
    /// block behind it, and no other hanging entity is in the way.
    /// </summary>
    internal async ValueTask<bool> SurvivesAsync(AbstractLevel level)
    {
        foreach (var cell in this.CoveredBlocks())
        {
            var behind = await level.GetBlockAsync(cell - this.Facing.ToVector());
            if (behind is null || !behind.IsSolid() && !IsDiode(behind))
                return false;

            if (await level.GetBlockAsync(cell) is not IBlock inside || inside.IsSolid())
                return false;
        }

        var bounds = this.BoundingBox;
        return !level.GetNonPlayerEntitiesInRange(this.Position, 4)
            .OfType<HangingEntity>()
            .Any(other => !ReferenceEquals(other, this) && other.BoundingBox.Intersects(bounds));
    }

    /// <summary>The blocks it covers, one for most, more for a large painting.</summary>
    protected virtual IEnumerable<Vector> CoveredBlocks() => [this.BlockPosition];

    /// <summary>
    /// Breaks it, as vanilla's <c>dropItem</c> does: it's removed with a sound, dropping its item unless
    /// <paramref name="dropItems"/> is off or <c>entity_drops</c> is.
    /// </summary>
    internal async ValueTask BreakAsync(bool dropItems)
    {
        if (this.broken)
            return;

        this.broken = true;
        this.PlaySound(this.BreakSound);

        if (dropItems && this.Level.LevelData.GetBooleanRule("entity_drops"))
            this.DropContents(withSelf: true);

        await this.RemoveAsync();
    }

    /// <summary>Drops what it holds, and itself with <paramref name="withSelf"/>.</summary>
    protected virtual void DropContents(bool withSelf)
    {
        if (withSelf)
            this.Level.SpawnItem(this.Position, new ItemStack(this.DropItem));
    }

    public virtual ValueTask<bool> InteractAsync(PlayerBase player, InteractionHand hand) => ValueTask.FromResult(false);

    /// <summary>Hitting it breaks it, dropping its item unless the player is in creative mode.</summary>
    public virtual ValueTask AttackedAsync(PlayerBase player) =>
        this.BreakAsync(dropItems: player.GameMode != GameMode.Creative);

    /// <summary>Plays one of its sounds where it hangs.</summary>
    protected void PlaySound(string sound) =>
        this.PacketBroadcaster.QueuePacketToLevelInRange(this.Level, this.Position, new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(this.Position.X, this.Position.Y, this.Position.Z),
            Category = SoundCategory.Blocks,
            Volume = 1,
            Pitch = 1,
            Seed = Random.Shared.NextInt64()
        });

    /// <summary>Tells players tracking it that its metadata changed.</summary>
    protected void SynchronizeMetadata() =>
        this.PacketBroadcaster.QueuePacketToLevelInRange(this.Level, this.Position,
            new SetEntityDataPacket { EntityId = this.EntityId, Entity = this });

    /// <summary>
    /// Places a hanging entity from the item a player uses on a block's face, as vanilla's
    /// <c>HangingEntityItem.useOn</c> does.
    /// </summary>
    /// <returns>Whether it was placed.</returns>
    internal static async ValueTask<bool> PlaceAsync(HangingEntity entity, AbstractLevel level, PlayerBase player, int slot)
    {
        entity.Level = level;
        entity.EntityId = Server.GetNextEntityId();

        if (!await entity.SurvivesAsync(level) || !level.TrySpawnEntity(entity))
            return false;

        entity.PlaySound(entity.PlaceSound);

        if (player.GameMode != GameMode.Creative)
        {
            player.Inventory.RemoveItem(slot, 1);
            await player.SyncInventorySlotAsync(slot);
        }

        return true;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(8, EntityMetadataType.Direction);
        writer.WriteVarInt(FacingId(this.Facing));
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.Set(new NbtTag<byte>("Facing", (byte)FacingId(this.Facing)));

        var block = this.BlockPosition;
        tag.Set(new NbtArray<int>("block_pos", [block.X, block.Y, block.Z]));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        var facing = FromFacingId(tag.TryGetTag<NbtTag<byte>>("Facing", out var saved) ? saved.Value : 0);
        var block = tag.TryGetTag<NbtArray<int>>("block_pos", out var position) && position.Count == 3
            ? new Vector(position.GetArray()[0], position.GetArray()[1], position.GetArray()[2])
            : (Vector)this.Position.Floor();

        // The position follows from the block, so variants and facings loaded in any order agree.
        this.Hang(block, facing);
    }

    private static bool IsDiode(IBlock block) => block.RegistryId is BlockIds.Repeater or BlockIds.Comparator;

    /// <summary>Vanilla's 3D direction ids: down, up, north, south, west, east.</summary>
    internal static int FacingId(BlockFace face) => face switch
    {
        BlockFace.Down => 0,
        BlockFace.Up => 1,
        BlockFace.North => 2,
        BlockFace.South => 3,
        BlockFace.West => 4,
        _ => 5
    };

    internal static BlockFace FromFacingId(int id) => id switch
    {
        0 => BlockFace.Down,
        1 => BlockFace.Up,
        2 => BlockFace.North,
        3 => BlockFace.South,
        4 => BlockFace.West,
        _ => BlockFace.East
    };

    /// <summary>The horizontal direction a quarter turn counterclockwise, seen from above.</summary>
    internal static BlockFace CounterClockwise(BlockFace face) => face switch
    {
        BlockFace.North => BlockFace.West,
        BlockFace.West => BlockFace.South,
        BlockFace.South => BlockFace.East,
        BlockFace.East => BlockFace.North,
        _ => face
    };
}
