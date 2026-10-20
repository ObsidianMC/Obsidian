using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// Lit TNT, like vanilla's <c>PrimedTnt</c>: it falls and slides like a block for its fuse, 80 ticks unless set
/// otherwise, then explodes with a power of 4.
/// </summary>
[MinecraftEntity("minecraft:tnt")]
public sealed partial class PrimedTnt : Entity
{
    /// <summary>Vanilla's fuse for TNT that's lit, in ticks.</summary>
    public const int DefaultFuse = 80;

    private const float Power = 4;

    private bool exploded;

    public PrimedTnt() => this.Type = EntityType.Tnt;

    /// <summary>The ticks left before it explodes.</summary>
    public int Fuse { get; set; } = DefaultFuse;

    /// <summary>The block it shows, TNT unless a plugin lit something else.</summary>
    public IBlock Block { get; set; } = BlocksRegistry.Tnt;

    /// <summary>Who lit it, who's blamed for its explosion.</summary>
    internal IEntity? Owner { get; set; }

    /// <summary>
    /// Lights the TNT at <paramref name="position"/>, as vanilla's <c>TntBlock.prime</c> does: the block becomes a
    /// primed TNT entity, with a little random nudge, that hisses as it's lit.
    /// </summary>
    /// <param name="fuse">The fuse, or <see langword="null"/> for vanilla's 80 ticks.</param>
    /// <returns>The lit TNT, or <see langword="null"/> where <c>tnt_explodes</c> is off.</returns>
    internal static PrimedTnt? Prime(AbstractLevel level, Vector position, IEntity? owner, int? fuse = null)
    {
        if (!level.LevelData.GetBooleanRule("tnt_explodes"))
            return null;

        var angle = Random.Shared.NextDouble() * Math.PI * 2;
        var tnt = new PrimedTnt
        {
            Level = level,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)position + new VectorD(0.5, 0, 0.5),
            Motion = new VectorD(-Math.Sin(angle) * 0.02, 0.2, -Math.Cos(angle) * 0.02),
            Owner = owner,
            Fuse = fuse ?? DefaultFuse
        };

        if (!level.TrySpawnEntity(tnt, hostOwned: true))
            return null;

        level.PacketBroadcaster.BroadcastToLevelInRange(level, position, new SoundPacket
        {
            SoundLocation = "minecraft:entity.tnt.primed",
            SoundPosition = new SoundPosition(tnt.Position.X, tnt.Position.Y, tnt.Position.Z),
            Category = SoundCategory.Blocks,
            Volume = 1,
            Pitch = 1,
            Seed = Random.Shared.NextInt64()
        });

        return tnt;
    }

    /// <summary>
    /// The fuse of TNT an explosion lit, as vanilla's <c>TntBlock.wasExploded</c> gives it: shorter, and random, so
    /// that chained TNT doesn't go off all at once.
    /// </summary>
    internal static int ExplodedFuse() => Random.Shared.Next(DefaultFuse / 4) + DefaultFuse / 8;

    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0) =>
        base.SpawnEntity(velocity ?? new Velocity(this.Motion.X, this.Motion.Y, this.Motion.Z), additionalData);

    public override async ValueTask TickAsync()
    {
        if (this.exploded || this.Level is not AbstractLevel level)
            return;

        var terrain = new MobTerrain(level);
        var next = EntityMovement.Move(this, terrain, VectorD.Zero, gravity: 0.04f, airFriction: 0.98f);

        // Like vanilla's, it slides less and bounces a little once it lands.
        if (this.MovementFlags.HasFlag(MovementFlags.OnGround))
            this.Motion = new VectorD(this.Motion.X * 0.7, this.Motion.Y * -0.5, this.Motion.Z * 0.7);

        if (next != this.Position && level.TryMoveEntity(this, this.Position, next))
            await this.UpdateAsync(next, this.MovementFlags);

        if (--this.Fuse > 0)
            return;

        this.exploded = true;
        await this.RemoveAsync();

        // Vanilla's explosion is a sixteenth of a block above the TNT's feet.
        var center = this.Position + new VectorD(0, 0.0625, 0);
        await level.ExplodeAsync(center, Power, this, this.Owner, AbstractLevel.ExplosionInteraction.Tnt);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(8, EntityMetadataType.VarInt);
        writer.WriteVarInt(this.Fuse);

        writer.WriteEntityMetadataType(9, EntityMetadataType.BlockState);
        writer.WriteVarInt(this.Block.GetHashCode());
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.Set(new NbtTag<short>("fuse", (short)Math.Clamp(this.Fuse, short.MinValue, short.MaxValue)));
        tag.Set(new NbtTag<float>("explosion_power", Power));
        if (this.Owner is { } owner)
            tag.Set(new NbtArray<int>("owner", EntityNbt.UuidToInts(owner.Uuid)));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        if (tag.TryGetTag<NbtTag<short>>("fuse", out var fuse))
            this.Fuse = fuse.Value;

        this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);
    }
}
