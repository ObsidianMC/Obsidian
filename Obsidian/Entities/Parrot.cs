using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:parrot")]
public sealed partial class Parrot : Animal
{
    public Parrot() => Type = EntityType.Parrot;
    public int Variant { get; internal set; }
    public Guid Owner { get; internal set; }
    public bool OrderedToSit { get; internal set; }
    public bool Tamed => Owner != Guid.Empty;
    protected override bool UsesAi => true;
    internal override bool FlyingNavigation => true;
    protected override bool TakesFallDamage => false;
    protected override string? SoundName => "parrot";
    protected override float DimensionScale => 1;
    protected override void FinalizeSpawn() => Variant = Random.Next(5);
    protected override VectorD Travel() => VolumeMovement.Travel(this, !OrderedToSit && Navigator is Navigator { IsNavigating: true } && !InWater);
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(1, new ParrotSitGoal(this));
        actions.AddGoal(2, new PanicGoal(this, 1.25f));
        actions.AddGoal(3, new ParrotFollowGoal(this));
        actions.AddGoal(6, new RandomStrollGoal(this, 1));
        actions.AddGoal(7, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(8, new RandomLookAroundGoal(this));
    }
    internal override async ValueTask InteractAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive || player.Health <= 0 || player.Level != Level || player.GameMode == GameMode.Spectator || !IsInRange(player, 4)) return;
        var item = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (item is { Count: > 0, Type: Material.Cookie })
        {
            await ConsumeInteractionItemAsync(player, hand);
            AddPotionEffect((int)PotionEffect.Poison - 1, 900);
            await DamageEnvironmentAsync(Health);
        }
        else if (!Tamed && item is { Count: > 0, Type: Material.WheatSeeds or Material.MelonSeeds or Material.PumpkinSeeds or Material.BeetrootSeeds or Material.TorchflowerSeeds or Material.PitcherPod })
        {
            await ConsumeInteractionItemAsync(player, hand);
            if (Random.Next(10) == 0) { Owner = player.Uuid; PersistenceRequired = true; }
            SendEntityEvent(Tamed ? (byte)7 : (byte)6);
            SynchronizeMetadata();
        }
        else if (Tamed && player.Uuid == Owner)
        {
            OrderedToSit = !OrderedToSit;
            ((Navigator)Navigator!).Stop();
            SynchronizeMetadata();
        }
    }
    protected override ValueTask OnHurtAsync(IEntity source)
    {
        OrderedToSit = false;
        SynchronizeMetadata();
        return default;
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (MobBitMask.HasFlag(MobBitmask.NoAi) || Random.Next(50) != 0) return;
        var mob = GetEntitiesNear(20).OfType<Mob>().FirstOrDefault(entity => entity.Hostile && !entity.Silent);
        if (mob != null)
        {
            var name = EntityNbt.TypeId(mob.Type)[10..];
            if (mob.Type is EntityType.Zombie or EntityType.Skeleton or EntityType.Creeper or EntityType.Spider or EntityType.CaveSpider or
                EntityType.Blaze or EntityType.Ghast or EntityType.Enderman or EntityType.Slime or EntityType.MagmaCube or EntityType.Silverfish or
                EntityType.Husk or EntityType.Stray or EntityType.Bogged)
                PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new Obsidian.Net.Packets.Play.Clientbound.SoundEntityPacket
                { EntityId = EntityId, SoundLocation = $"minecraft:entity.parrot.imitate.{name}", Category = SoundCategory.Neutral,
                    Volume = 0.7f, Pitch = 0.8f + Random.NextSingle() * 0.4f, Seed = Random.NextInt64() });
        }
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        DropItem(Material.Feather, Random.Next(1, 3));
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Byte);
        writer.WriteByte((byte)((Tamed ? 4 : 0) | (OrderedToSit ? 1 : 0)));
        writer.WriteEntityMetadataType(18, EntityMetadataType.OptionalLivingEntityReference);
        writer.WriteOptional(Tamed ? (Guid?)Owner : null);
        writer.WriteEntityMetadataType(19, EntityMetadataType.VarInt);
        writer.WriteVarInt(Variant);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteInt("Variant", Variant);
        writer.WriteBool("Sitting", OrderedToSit);
        if (Tamed) writer.WriteArray("Owner", EntityNbt.UuidToInts(Owner));
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        Variant = Math.Clamp((tag.TryGetTagValue<int>("Variant", out var savedVariant) ? savedVariant : 0), 0, 4);
        OrderedToSit = tag.TryGetBool("Sitting", out var sitting) && sitting;
        if (tag.TryGetTag<NbtArray<int>>("Owner", out var owner) && owner.Count == 4) Owner = EntityNbt.UuidFromInts(owner.GetArray());
        PersistenceRequired |= Tamed;
    }
}

internal sealed class ParrotSitGoal(Parrot parrot) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Jump;
    public override bool CanUse() => parrot.Tamed && parrot.OrderedToSit && !parrot.InWater;
    public override void Start() => ((Navigator)parrot.Navigator!).Stop();
}

internal sealed class ParrotFollowGoal(Parrot parrot) : NavigationGoal(parrot, 1)
{
    private IPlayer? owner;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse()
    {
        owner = parrot.Level.Players.Values.FirstOrDefault(player => player.Uuid == parrot.Owner && player.Health > 0 && player.GameMode != GameMode.Spectator);
        return !parrot.OrderedToSit && owner != null && (owner.Position - parrot.Position).MagnitudeSquared() > 25;
    }
    public override bool CanContinue() => !parrot.OrderedToSit && owner != null && owner.Level == parrot.Level && owner.Health > 0 &&
        owner.GameMode != GameMode.Spectator && (owner.Position - parrot.Position).MagnitudeSquared() > 4;
    public override ValueTask TickAsync() { MoveTo(owner!.Position + new VectorD(0, 1, 0)); return default; }
}
