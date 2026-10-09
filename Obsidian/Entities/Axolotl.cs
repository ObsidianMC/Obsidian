using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:axolotl")]
public sealed partial class Axolotl : FarmAnimal
{
    public Axolotl() => Type = EntityType.Axolotl;
    public int Variant { get; internal set; }
    public bool FromBucket { get; internal set; }
    public int PlayingDeadTicks { get; internal set; }
    private int huntingCooldown;
    private int dryTicks;
    internal override bool SwimmingNavigation => true;
    protected override bool UsesFloatGoal => false;
    protected override bool CanDespawn => !FromBucket;
    internal override bool PanicsWhenHurt => false;
    protected override string? SoundName => "axolotl";
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0, Type: Material.TropicalFishBucket };
    protected override void FinalizeSpawn() => Variant = Random.Next(4);
    protected override VectorD Travel() => VolumeMovement.Travel(this, InWater);
    internal override async ValueTask InteractAsync(IPlayer player, InteractionHand hand)
    {
        if (!await TryCaptureInBucketAsync(player, hand, Material.AxolotlBucket)) await base.InteractAsync(player, hand);
    }
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(0, new AxolotlPlayDeadGoal(this));
        actions.AddGoal(1, new MeleeAttackGoal(this, 1.5f));
        actions.AddGoal(5, new SeekWaterGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, target => InWater && PlayingDeadTicks == 0 && huntingCooldown == 0 &&
            target.Type is EntityType.Squid or EntityType.GlowSquid or EntityType.Tadpole or EntityType.Cod or EntityType.Salmon or
                EntityType.TropicalFish or EntityType.Pufferfish or EntityType.Drowned or EntityType.Guardian or EntityType.ElderGuardian));
    }
    protected override IEntity CreateOffspring(Animal mate)
    {
        var child = (Axolotl)base.CreateOffspring(mate);
        child.Variant = Random.Next(1200) == 0 ? 4 : Random.Next(2) == 0 ? Variant : ((Axolotl)mate).Variant;
        child.SynchronizeMetadata();
        return child;
    }
    internal override async ValueTask FeedAsync(IPlayer player, InteractionHand hand)
    {
        var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
        var item = player.Inventory.GetItem(slot);
        var previousCount = item?.Count ?? 0;
        var food = CanEat(item);
        await base.FeedAsync(player, hand);
        if (food && player.GameMode != GameMode.Creative && (player.Inventory.GetItem(slot)?.Count ?? 0) < previousCount)
        {
            player.Inventory.SetItem(slot, ItemsRegistry.GetSingleItem(Material.WaterBucket));
            await player.Client.QueuePacketAsync(new Obsidian.Net.Packets.Play.Clientbound.ContainerSetSlotPacket
            { ContainerId = 0, Slot = (short)slot, SlotData = player.Inventory.GetItem(slot) });
        }
    }
    protected override ValueTask OnHurtAsync(IEntity source)
    {
        if (InWater && PlayingDeadTicks == 0 && Random.Next(3) == 0 && Health < GetAttributeValue("minecraft:generic.max_health") / 2)
        {
            PlayingDeadTicks = 200;
            AttackTarget = null;
            AddPotionEffect((int)PotionEffect.Regeneration - 1, 200);
            SynchronizeMetadata();
        }
        return default;
    }
    protected internal override async ValueTask PerformMeleeAttackAsync(IEntity target)
    {
        await base.PerformMeleeAttackAsync(target);
        if (target.Health <= 0)
        {
            huntingCooldown = 2400;
            AttackTarget = null;
            foreach (var player in Level.GetPlayersInRange(Position, 20).OfType<Living>())
            {
                if (LastAttacker is not IPlayer && target is Mob { LastAttacker: IPlayer helper } && helper.Uuid == player.Uuid)
                {
                    player.RemovePotionEffect((int)PotionEffect.MiningFatigue - 1);
                    player.AddPotionEffect((int)PotionEffect.Regeneration - 1, 100);
                }
            }
        }
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (PlayingDeadTicks > 0)
        {
            AttackTarget = null;
            if (PlayingDeadTicks % 50 == 0) Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + 1);
            if (--PlayingDeadTicks == 0) SynchronizeMetadata();
        }
        if (huntingCooldown > 0) huntingCooldown--;
        if (InWater || Terrain.IsRainingAt((Vector)Position.Floor())) dryTicks = 0;
        else if (++dryTicks >= 6000 && dryTicks % 20 == 0) await DamageEnvironmentAsync(2);
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.VarInt);
        writer.WriteVarInt(Variant);
        writer.WriteEntityMetadataType(18, EntityMetadataType.Boolean);
        writer.WriteBoolean(PlayingDeadTicks > 0);
        writer.WriteEntityMetadataType(19, EntityMetadataType.Boolean);
        writer.WriteBoolean(FromBucket);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteInt("Variant", Variant);
        writer.WriteBool("FromBucket", FromBucket);
        writer.WriteInt("ObsidianPlayDead", PlayingDeadTicks);
        writer.WriteInt("ObsidianHuntingCooldown", huntingCooldown);
        writer.WriteInt("ObsidianDryTicks", dryTicks);
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        Variant = Math.Clamp((tag.TryGetTagValue<int>("Variant", out var savedVariant) ? savedVariant : 0), 0, 4);
        FromBucket = tag.TryGetBool("FromBucket", out var bucket) && bucket;
        PersistenceRequired |= FromBucket;
        PlayingDeadTicks = Math.Clamp((tag.TryGetTagValue<int>("ObsidianPlayDead", out var savedObsidianPlayDead) ? savedObsidianPlayDead : 0), 0, 200);
        huntingCooldown = Math.Clamp((tag.TryGetTagValue<int>("ObsidianHuntingCooldown", out var savedObsidianHuntingCooldown) ? savedObsidianHuntingCooldown : 0), 0, 2400);
        dryTicks = Math.Max(0, (tag.TryGetTagValue<int>("ObsidianDryTicks", out var savedObsidianDryTicks) ? savedObsidianDryTicks : 0));
    }
}

internal sealed class AxolotlPlayDeadGoal(Axolotl axolotl) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Jump | GoalFlags.Look;
    public override bool CanUse() => axolotl.PlayingDeadTicks > 0 && axolotl.InWater;
    public override void Start() => ((Navigator)axolotl.Navigator!).Stop();
    public override ValueTask TickAsync() { axolotl.Motion = VectorD.Zero; return default; }
}
