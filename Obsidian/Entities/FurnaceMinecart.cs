using Obsidian.API.Inventory;
using Obsidian.Nbt;

namespace Obsidian.Entities;

/// <summary>
/// A minecart with a furnace, like vanilla's <c>MinecartFurnace</c>: coal or charcoal fed to it burns for 3600 ticks
/// each, up to 32000, pushing it away from the player who fed it at half a minecart's speed, and it pushes the carts it
/// runs into.
/// </summary>
[MinecraftEntity("minecraft:furnace_minecart")]
public sealed partial class FurnaceMinecart : AbstractMinecart
{
    private const int FuelPerItem = 3600;
    private const int MaxFuel = 32000;

    private bool hasFuel;

    public FurnaceMinecart() => this.Type = EntityType.FurnaceMinecart;

    /// <summary>The ticks of fuel left (vanilla's <c>Fuel</c>).</summary>
    public int Fuel { get; set; }

    /// <summary>The way the burning furnace pushes it (vanilla's <c>PushX</c> and <c>PushZ</c>).</summary>
    public VectorD Push { get; set; }

    internal override bool IsFurnace => true;

    protected override Item DropItem => ItemsRegistry.Get(ItemIds.FurnaceMinecart);

    protected override double MaxSpeed => base.MaxSpeed * (this.InWater ? 0.75 : 0.5);

    // Vanilla's MinecartFurnace.interact: fuel goes in, and the cart is pushed away from the player; the use counts
    // whatever the player holds.
    public override async ValueTask<bool> InteractAsync(PlayerBase player, InteractionHand hand)
    {
        var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
        var item = player.Inventory.GetItem(slot);
        if (item is { Count: > 0 } fuel
            && TagsRegistry.Item.FurnaceMinecartFuel.Entries.Contains(fuel.Holder.Id)
            && this.Fuel + FuelPerItem <= MaxFuel)
        {
            this.Fuel += FuelPerItem;
            if (this.Fuel > 0)
            {
                var away = this.Position - player.Position;
                this.Push = new VectorD(away.X, 0, away.Z);
            }

            if (player.GameMode != GameMode.Creative)
            {
                player.Inventory.RemoveItem(slot, 1);
                await player.SyncInventorySlotAsync(slot);
            }
        }

        return true;
    }

    private protected override ValueTask AfterMoveAsync()
    {
        if (this.Fuel > 0)
            this.Fuel--;
        if (this.Fuel <= 0)
            this.Push = VectorD.Zero;

        var burning = this.Fuel > 0;
        if (burning != this.hasFuel)
        {
            this.hasFuel = burning;
            this.DataChanged = true;
        }

        return default;
    }

    // Vanilla's MinecartFurnace.applyNaturalSlowdown: a burning furnace keeps pushing along the rail, its push turned
    // the way the cart goes; then the usual slowdown.
    private protected override VectorD ApplyNaturalSlowdown(VectorD motion)
    {
        VectorD slowed;
        if (this.Push.MagnitudeSquared() > 1.0E-7)
        {
            this.Push = this.PushAlong(motion);
            slowed = new VectorD(motion.X * 0.8, 0, motion.Z * 0.8) + this.Push;
            if (this.InWater)
                slowed *= 0.1;
        }
        else
        {
            slowed = new VectorD(motion.X * 0.98, 0, motion.Z * 0.98);
        }

        return base.ApplyNaturalSlowdown(slowed);
    }

    // Vanilla's calculateNewPushAlong: the push projected onto the motion, keeping its strength.
    private VectorD PushAlong(VectorD motion)
    {
        var push = this.Push;
        var pushHorizontal = push.X * push.X + push.Z * push.Z;
        var motionHorizontal = motion.X * motion.X + motion.Z * motion.Z;
        if (pushHorizontal <= 1.0E-4 || motionHorizontal <= 0.001)
            return push;

        var projected = motion * (VectorD.Dot(push, motion) / VectorD.Dot(motion, motion));
        var length = projected.Magnitude;
        return length < 1.0E-4 ? VectorD.Zero : projected / length * push.Magnitude;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(13, EntityMetadataType.Boolean);
        writer.WriteBoolean(this.hasFuel);

        this.MetadataIndex = 14;
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        tag.Set(new NbtTag<double>("PushX", this.Push.X));
        tag.Set(new NbtTag<double>("PushZ", this.Push.Z));
        tag.Set(new NbtTag<short>("Fuel", (short)this.Fuel));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        var x = tag.TryGetTagValue<double>("PushX", out var pushX) ? pushX : 0;
        var z = tag.TryGetTagValue<double>("PushZ", out var pushZ) ? pushZ : 0;
        this.Push = new VectorD(x, 0, z);
        this.Fuel = tag.TryGetTagValue<short>("Fuel", out var fuel) ? fuel : 0;
        this.hasFuel = this.Fuel > 0;
    }
}
