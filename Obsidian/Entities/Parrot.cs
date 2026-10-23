using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:parrot")]
public sealed partial class Parrot : Animal
{
    internal static readonly IReadOnlyDictionary<EntityType, string> ImitationSounds = new Dictionary<EntityType, string>
    {
        [EntityType.Blaze] = "blaze",
        [EntityType.Bogged] = "bogged",
        [EntityType.Breeze] = "breeze",
        [EntityType.CamelHusk] = "camel_husk",
        [EntityType.CaveSpider] = "spider",
        [EntityType.Creaking] = "creaking",
        [EntityType.Creeper] = "creeper",
        [EntityType.Drowned] = "drowned",
        [EntityType.ElderGuardian] = "elder_guardian",
        [EntityType.EnderDragon] = "ender_dragon",
        [EntityType.Endermite] = "endermite",
        [EntityType.Evoker] = "evoker",
        [EntityType.Ghast] = "ghast",
        [EntityType.HappyGhast] = "",
        [EntityType.Guardian] = "guardian",
        [EntityType.Hoglin] = "hoglin",
        [EntityType.Husk] = "husk",
        [EntityType.Illusioner] = "illusioner",
        [EntityType.MagmaCube] = "magma_cube",
        [EntityType.Parched] = "parched",
        [EntityType.Phantom] = "phantom",
        [EntityType.Piglin] = "piglin",
        [EntityType.PiglinBrute] = "piglin_brute",
        [EntityType.Pillager] = "pillager",
        [EntityType.Ravager] = "ravager",
        [EntityType.Shulker] = "shulker",
        [EntityType.Silverfish] = "silverfish",
        [EntityType.Skeleton] = "skeleton",
        [EntityType.Slime] = "slime",
        [EntityType.Spider] = "spider",
        [EntityType.Stray] = "stray",
        [EntityType.Vex] = "vex",
        [EntityType.Vindicator] = "vindicator",
        [EntityType.Warden] = "warden",
        [EntityType.Witch] = "witch",
        [EntityType.Wither] = "wither",
        [EntityType.WitherSkeleton] = "wither_skeleton",
        [EntityType.Zoglin] = "zoglin",
        [EntityType.Zombie] = "zombie",
        [EntityType.ZombieHorse] = "zombie_horse",
        [EntityType.ZombieNautilus] = "zombie_nautilus",
        [EntityType.ZombieVillager] = "zombie_villager"
    };

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

    internal override async ValueTask<bool> InteractAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive
            || player.Health <= 0
            || player.Level != Level
            || player.GameMode == GameMode.Spectator
            || !IsInRange(player, 4))
            return false;
        var item = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (item is { Count: > 0, Holder.Id: ItemIds.Cookie })
        {
            await ConsumeInteractionItemAsync(player, hand);
            AddPotionEffect((int)PotionEffect.Poison - 1, 900);
            await DamageEnvironmentAsync(Health);
        }
        else if (!Tamed
            && item is
            {
                Count: > 0,
                Holder.Id: ItemIds.WheatSeeds
                    or ItemIds.MelonSeeds
                    or ItemIds.PumpkinSeeds
                    or ItemIds.BeetrootSeeds
                    or ItemIds.TorchflowerSeeds
                    or ItemIds.PitcherPod
            })
        {
            await ConsumeInteractionItemAsync(player, hand);
            if (Random.Next(10) == 0)
            {
                Owner = player.Uuid;
                PersistenceRequired = true;
            }
            SendEntityEvent(Tamed ? (byte)7 : (byte)6);
            SynchronizeMetadata();
        }
        else if (Tamed && player.Uuid == Owner)
        {
            OrderedToSit = !OrderedToSit;
            ((Navigator)Navigator!).Stop();
            SynchronizeMetadata();
        }
        else
            return false;

        return true;
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
        if (Random.Next(400) != 0 || !Alive || Silent || Random.Next(2) != 0)
            return;
        var bounds = Dimension.CreateBBFromPosition(Position);
        var nearby = Level.GetEntitiesInRange(Position, float.MaxValue).OfType<Mob>().Where(mob => mob.Type is EntityType type && ImitationSounds.ContainsKey(type) &&
            mob.BoundingBox.Max.X > bounds.Min.X - 20 && mob.BoundingBox.Min.X < bounds.Max.X + 20 &&
            mob.BoundingBox.Max.Y > bounds.Min.Y - 20 && mob.BoundingBox.Min.Y < bounds.Max.Y + 20 &&
            mob.BoundingBox.Max.Z > bounds.Min.Z - 20 && mob.BoundingBox.Min.Z < bounds.Max.Z + 20).ToArray();
        if (nearby.Length > 0 && nearby[Random.Next(nearby.Length)] is { Silent: false } selected)
            PlayImitation(ImitationSounds[selected.Type!.Value], 0.7f);
    }

    protected internal override void PlayMobSound(string kind)
    {
        if (kind != "ambient")
        {
            base.PlayMobSound(kind);
            return;
        }
        if (Silent)
            return;
        if (Level.LevelData.Difficulty != Difficulty.Peaceful && Random.Next(1000) == 0)
            PlayImitation(ImitationSounds.Values.ElementAt(Random.Next(ImitationSounds.Count)), 1);
        else
            base.PlayMobSound(kind);
    }

    private void PlayImitation(string sound, float volume) => PacketBroadcaster.QueuePacketToLevelInRange(Level, Position,
        new Obsidian.Net.Packets.Play.Clientbound.SoundEntityPacket
        {
            EntityId = EntityId,
            SoundLocation = sound.Length == 0 ? "minecraft:intentionally_empty" : $"minecraft:entity.parrot.imitate.{sound}",
            Category = SoundCategory.Neutral,
            Volume = volume,
            Pitch = (Random.NextSingle() - Random.NextSingle()) * 0.2f + 1,
            Seed = Random.NextInt64()
        });

    protected override ValueTask OnDeathAsync(IEntity source)
    {
        DropItem(ItemsRegistry.Feather, Random.Next(1, 3));
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
        if (Tamed)
            writer.WriteArray("Owner", EntityNbt.UuidToInts(Owner));
    }

    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        Variant = Math.Clamp((tag.TryGetTagValue<int>("Variant", out var savedVariant) ? savedVariant : 0), 0, 4);
        OrderedToSit = tag.TryGetBool("Sitting", out var sitting) && sitting;
        if (tag.TryGetTag<NbtArray<int>>("Owner", out var owner) && owner.Count == 4)
            Owner = EntityNbt.UuidFromInts(owner.GetArray());
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
    private long nextPath;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool RequiresUpdateEveryTick => true;

    public override bool CanUse()
    {
        owner = parrot.Level.Players.Values.FirstOrDefault(player => player.Uuid == parrot.Owner && player.Health > 0 && player.GameMode != GameMode.Spectator);
        return parrot.Tamed && !parrot.OrderedToSit && owner != null && (owner.Position - parrot.Position).MagnitudeSquared() >= 25;
    }

    public override bool CanContinue() => Navigation.IsNavigating && !parrot.OrderedToSit && owner != null && owner.Level == parrot.Level && owner.Health > 0 &&
        owner.GameMode != GameMode.Spectator && (owner.Position - parrot.Position).MagnitudeSquared() > 4;

    public override void Start() => nextPath = 0;

    public override void Stop()
    {
        owner = null;
        base.Stop();
    }

    public override ValueTask TickAsync()
    {
        var teleport = (owner!.Position - parrot.Position).MagnitudeSquared() >= 144;
        if (!teleport)
            parrot.LookControl.LookAt(owner);
        if (parrot.AiTick < nextPath)
            return default;
        nextPath = parrot.AiTick + 10;
        if (!teleport)
        {
            MoveTo(owner);
            return default;
        }
        if (parrot.Level is not AbstractLevel level)
            return default;
        var origin = (Vector)owner.Position.Floor();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var x = parrot.Random.Next(-3, 4);
            var z = parrot.Random.Next(-3, 4);
            if (Math.Abs(x) < 2 && Math.Abs(z) < 2)
                continue;
            var point = new Vector(origin.X + x, origin.Y + parrot.Random.Next(-1, 2), origin.Z + z);
            var destination = new VectorD(point.X + 0.5f, point.Y, point.Z + 0.5f);
            var floor = parrot.Terrain.GetBlock(point - new Vector(0, 1, 0));
            if (floor == null || floor.IsLiquid || BlockCollisionShapes.Get(floor).Count == 0 ||
                new WalkNodeEvaluator(parrot).GetCost(destination) != 0 ||
                !parrot.Terrain.IsFree(parrot.Dimension.CreateBBFromPosition(destination)) ||
                !level.TryMoveEntity(parrot, parrot.Position, destination))
                continue;
            parrot.CompleteTeleport(destination);
            Navigation.Stop();
            break;
        }
        return default;
    }
}
