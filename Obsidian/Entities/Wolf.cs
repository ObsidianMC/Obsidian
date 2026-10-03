using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:wolf")]
public sealed partial class Wolf : Animal
{
    internal static readonly string[] VariantNames = ["ashen", "black", "chestnut", "pale", "rusty", "snowy", "spotted", "striped", "woods"];
    internal static readonly string[] SoundVariantNames = ["angry", "big", "classic", "cute", "grumpy", "puglin", "sad"];
    private Guid owner;
    private byte collarColor = 14;
    private int variant = 3;
    private int soundVariant = 2;
    internal Guid AngryAt { get; set; }
    private bool begging;
    private bool wet;
    private int shakeTicks;
    public Wolf() => Type = EntityType.Wolf;
    public Guid Owner
    {
        get => owner;
        set
        {
            owner = value;
            TryUpdateAttribute("minecraft:generic.max_health", Tamed ? 40 : 8);
            TryUpdateAttribute("minecraft:generic.attack_damage", 4);
        }
    }
    public bool Tamed => Owner != Guid.Empty;
    public bool OrderedToSit { get; set; }
    internal bool IsSitting { get; set; }
    internal long AngerTicks { get; set; }
    public byte CollarColor
    {
        get => collarColor;
        set => collarColor = value < 16 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
    public int Variant
    {
        get => variant;
        set => variant = (uint)value < 9 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
    public int SoundVariant
    {
        get => soundVariant;
        set => soundVariant = (uint)value < 7 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
    protected override bool UsesAi => true;
    protected override string? SoundName => SoundVariant == 2 ? "wolf" : $"wolf_{SoundVariantNames[SoundVariant]}";
    protected override bool CanEat(ItemStack? item) => Tamed && item is { Count: > 0 } &&
        TagsRegistry.Item.WolfFood.Entries.Contains(item.Holder.Id);
    protected override int GetExperienceReward() => IsBaby ? 0 : Random.Next(1, 4);
    protected override void FinalizeSpawn()
    {
        var point = (Vector)Position.Floor();
        var name = (Level as AbstractLevel)?.GetLoadedChunk(point.X >> 4, point.Z >> 4)?.GetBiome(point.X, point.Y, point.Z).Name;
        Variant = name switch
        {
            "minecraft:snowy_taiga" => 0,
            "minecraft:old_growth_pine_taiga" => 1,
            "minecraft:old_growth_spruce_taiga" => 2,
            "minecraft:jungle" or "minecraft:sparse_jungle" or "minecraft:bamboo_jungle" => 4,
            "minecraft:grove" => 5,
            "minecraft:savanna" or "minecraft:savanna_plateau" or "minecraft:windswept_savanna" => 6,
            "minecraft:badlands" or "minecraft:wooded_badlands" or "minecraft:eroded_badlands" => 7,
            "minecraft:forest" => 8,
            _ => 3
        };
        SoundVariant = Random.Next(7);
    }
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new FloatGoal(this));
        actions.AddGoal(2, new WolfSitGoal(this));
        actions.AddGoal(3, new AvoidEntityGoal(this, entity => entity.Type == EntityType.Llama && !Tamed, 24, 1.5f));
        actions.AddGoal(4, new MeleeAttackGoal(this));
        actions.AddGoal(5, new WolfFollowOwnerGoal(this));
        actions.AddGoal(6, new BreedGoal(this));
        actions.AddGoal(7, new RandomStrollGoal(this, 1));
        actions.AddGoal(8, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(9, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new WolfRetaliateGoal(this));
        targets.AddGoal(4, new NearestAttackableTargetGoal(this, entity => !Tamed && !OrderedToSit &&
            entity.Type is EntityType.Sheep or EntityType.Rabbit or EntityType.Fox));
        targets.AddGoal(5, new NearestAttackableTargetGoal(this, entity => !OrderedToSit &&
            entity.Type is EntityType.Skeleton or EntityType.Stray or EntityType.Bogged or EntityType.Parched));
    }
    internal bool CanAttack(IEntity target) => IsValidTarget(target) && target.Type is not EntityType.Creeper and not EntityType.Ghast &&
        (target is not IPlayer player || player.Uuid != Owner) &&
        (target is not Wolf wolf || !wolf.Tamed || wolf.Owner != Owner) &&
        (target is not AbstractHorse horse || !horse.HorseMask.HasFlag(HorseMask.Tamed)) &&
        target is not Cat { Tamed: true };
    internal static void AlertOwnedWolves(IPlayer player, IEntity target)
    {
        foreach (var wolf in player.Level.GetEntitiesInRange(player.Position, 16).OfType<Wolf>())
            if (wolf.Owner == player.Uuid && wolf.CanAttack(target))
            {
                wolf.OrderedToSit = false;
                wolf.AlertedTarget = target;
            }
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (!Tamed || OrderedToSit || Health < GetAttributeValue("minecraft:generic.max_health"))
            LoveTicks = 0;
        if (!MobBitMask.HasFlag(MobBitmask.NoAi))
        {
            var interested = Level.GetPlayersInRange(Position, 8).FirstOrDefault(player => player.Health > 0 &&
                player.Gamemode != Gamemode.Spectator && (CanEat(player.GetHeldItem()) || CanEat(player.GetOffHandItem()) ||
                    !Tamed && (player.GetHeldItem()?.Type == Material.Bone || player.GetOffHandItem()?.Type == Material.Bone)));
            if (interested != null && AttackTarget == null) LookControl.LookAt(interested);
            if (begging != (interested != null)) { begging = interested != null; SynchronizeMetadata(); }
            if (!Tamed && AngerTicks > 0 && AttackTarget == null && Level.LevelData.Difficulty != Difficulty.Peaceful)
                AlertedTarget = Level.GetPlayersInRange(Position, FollowRange).FirstOrDefault(player => player.Uuid == AngryAt && CanAttack(player));
        }
        if (AngerTicks > 0 && --AngerTicks == 0)
        {
            AttackTarget = null;
            AngryAt = Guid.Empty;
            SetAggressive(false);
            SynchronizeMetadata();
        }
        if (Level.LevelData.Difficulty == Difficulty.Peaceful && AttackTarget is IPlayer)
        {
            AttackTarget = null;
            AngerTicks = 0;
            SetAggressive(false);
        }
        if (InWater || Terrain.IsRainingAt((Vector)Position.Floor()))
        {
            wet = true;
            shakeTicks = 0;
        }
        else if (wet && MovementFlags.HasFlag(MovementFlags.OnGround))
        {
            if (shakeTicks++ == 0) { SendEntityEvent(8); PlayMobSound("shake"); }
            if (shakeTicks >= 40) { wet = false; shakeTicks = 0; }
        }
    }
    protected override ValueTask OnHurtAsync(IEntity source)
    {
        OrderedToSit = false;
        if (!Tamed && !ReferenceEquals(source, this) && IsValidTarget(source))
        {
            AngerTicks = Random.Next(400, 800);
            AngryAt = source.Uuid;
            SynchronizeMetadata();
        }
        return default;
    }
    internal override async ValueTask FeedAsync(IPlayer player, Hand hand)
    {
        if (!Alive || IsRemoved || player.Health <= 0 || player.Level != Level || player.Gamemode == Gamemode.Spectator ||
            !IsInRange(player, 3) || !CanSee(player))
            return;
        var item = hand == Hand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (!Tamed && AngerTicks == 0 && item?.Type == Material.Bone)
        {
            await ConsumeInteractionItemAsync(player, hand);
            if (Random.Next(3) == 0)
            {
                Owner = player.Uuid;
                Health = 40;
                PersistenceRequired = true;
                OrderedToSit = true;
                AttackTarget = null;
            }
            SendEntityEvent(Tamed ? (byte)7 : (byte)6);
            SynchronizeMetadata();
            return;
        }
        if (CanEat(item))
        {
            if (Health < GetAttributeValue("minecraft:generic.max_health"))
            {
                var food = item!.GetComponent<Obsidian.API.Inventory.DataComponents.FoodDataComponent>(DataComponentType.Food);
                Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + (food?.Nutrition ?? 1));
                await ConsumeInteractionItemAsync(player, hand);
                SynchronizeMetadata();
            }
            else if (!OrderedToSit)
                await base.FeedAsync(player, hand);
            return;
        }
        if (!Tamed || Owner != player.Uuid)
            return;
        var color = item == null ? -1 : Array.FindIndex(Cat.DyeNames, name => item.Holder.UnlocalizedName == $"minecraft:{name}_dye");
        if (color >= 0)
        {
            if (CollarColor != color)
            {
                CollarColor = (byte)color;
                await ConsumeInteractionItemAsync(player, hand);
            }
        }
        else
        {
            OrderedToSit = !OrderedToSit;
            AttackTarget = null;
            ((Navigator)Navigator!).Stop();
        }
        SynchronizeMetadata();
    }
    protected override IEntity CreateOffspring(Animal mate)
    {
        var child = (Wolf)base.CreateOffspring(mate);
        child.Owner = Owner;
        child.Health = Tamed ? 40 : 8;
        child.PersistenceRequired = Tamed;
        child.CollarColor = CollarColor;
        child.Variant = Random.Next(2) == 0 ? Variant : ((Wolf)mate).Variant;
        child.SoundVariant = Random.Next(2) == 0 ? SoundVariant : ((Wolf)mate).SoundVariant;
        child.SynchronizeMetadata();
        return child;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Byte);
        writer.WriteByte((byte)((IsSitting ? 1 : 0) | (Tamed ? 4 : 0)));
        writer.WriteEntityMetadataType(18, EntityMetadataType.OptionalLivingEntityReference);
        writer.WriteOptional(Tamed ? (Guid?)Owner : null);
        writer.WriteEntityMetadataType(19, EntityMetadataType.Boolean);
        writer.WriteBoolean(begging);
        writer.WriteEntityMetadataType(20, EntityMetadataType.VarInt);
        writer.WriteVarInt(CollarColor);
        writer.WriteEntityMetadataType(21, EntityMetadataType.VarLong);
        writer.WriteVarLong(AngerTicks == 0 ? 0 : Level.LevelData.Time + AngerTicks);
        writer.WriteEntityMetadataType(22, EntityMetadataType.WolfVariant);
        writer.WriteVarInt(Variant);
        writer.WriteEntityMetadataType(23, EntityMetadataType.WolfSoundVariant);
        writer.WriteVarInt(SoundVariant);
    }
}

internal sealed class WolfSitGoal(Wolf wolf) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Jump;
    public override bool CanUse() => wolf.Tamed && wolf.OrderedToSit && !wolf.InWater && wolf.MovementFlags.HasFlag(MovementFlags.OnGround);
    public override void Start()
    {
        var navigation = (Navigator)wolf.Navigator!;
        navigation.Stop(); navigation.IsPaused = true;
        wolf.IsSitting = true; wolf.SynchronizeMetadata();
    }
    public override void Stop()
    {
        var navigation = (Navigator)wolf.Navigator!;
        navigation.Stop(); navigation.IsPaused = false;
        wolf.IsSitting = false; wolf.SynchronizeMetadata();
    }
}

internal sealed class WolfFollowOwnerGoal(Wolf wolf) : NavigationGoal(wolf, 1)
{
    private IPlayer? owner;
    private long nextPath;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool CanUse()
    {
        owner = wolf.Level.Players.Values.FirstOrDefault(player => player.Uuid == wolf.Owner && player.Health > 0 && player.Gamemode != Gamemode.Spectator);
        return wolf.Tamed && !wolf.OrderedToSit && wolf.AttackTarget == null && owner != null && (owner.Position - wolf.Position).MagnitudeSquared() > 100;
    }
    public override bool CanContinue() => owner != null && owner.Level == wolf.Level && owner.Health > 0 && owner.Gamemode != Gamemode.Spectator &&
        !wolf.OrderedToSit && wolf.AttackTarget == null && (owner.Position - wolf.Position).MagnitudeSquared() > 4;
    public override ValueTask TickAsync()
    {
        wolf.LookControl.LookAt(owner!);
        if (wolf.AiTick < nextPath) return default;
        nextPath = wolf.AiTick + 10;
        if ((owner!.Position - wolf.Position).MagnitudeSquared() > 144 && wolf.Level is AbstractLevel level)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var point = (Vector)(owner.Position + new VectorF(wolf.Random.Next(-3, 4), wolf.Random.Next(-1, 2), wolf.Random.Next(-3, 4))).Floor();
                var floor = wolf.Terrain.GetBlock(new Vector(point.X, point.Y - 1, point.Z));
                var destination = new VectorF(point.X + 0.5f, point.Y, point.Z + 0.5f);
                if ((destination - owner.Position).MagnitudeSquared() < 4 || floor == null || floor.IsLiquid ||
                    BlockCollisionShapes.Get(floor).Count == 0 || !wolf.Terrain.IsFree(wolf.Dimension.CreateBBFromPosition(destination)) ||
                    !level.TryMoveEntity(wolf, wolf.Position, destination)) continue;
                wolf.CompleteTeleport(destination);
                return default;
            }
        }
        MoveTo(owner);
        return default;
    }
}

internal sealed class WolfRetaliateGoal(Wolf wolf) : Goal
{
    private long handledTick = -100;
    private IEntity? candidate;
    public override GoalFlags Flags => GoalFlags.Target;
    public override bool CanUse()
    {
        candidate = wolf.AlertedTarget ?? (handledTick != wolf.LastHurtTick ? wolf.LastAttacker : null);
        return candidate != null && wolf.CanAttack(candidate);
    }
    public override void Start()
    {
        handledTick = wolf.LastHurtTick;
        wolf.AttackTarget = candidate;
        if (!wolf.Tamed)
        {
            wolf.AngryAt = candidate!.Uuid;
            if (wolf.AngerTicks == 0) wolf.AngerTicks = wolf.Random.Next(400, 800);
            wolf.SynchronizeMetadata();
        }
        wolf.AlertedTarget = null;
        wolf.OrderedToSit = false;
        if (!wolf.Tamed)
            foreach (var ally in wolf.GetEntitiesNear(16).OfType<Wolf>().Where(ally => !ally.Tamed && ally.CanAttack(candidate!)))
            { ally.AlertedTarget = candidate; ally.AngerTicks = wolf.AngerTicks; ally.AngryAt = candidate!.Uuid; }
    }
    public override bool CanContinue() => wolf.AttackTarget is { } target && wolf.CanAttack(target) &&
        (wolf.Tamed || wolf.AngerTicks > 0) && (target.Position - wolf.Position).MagnitudeSquared() < wolf.FollowRange * wolf.FollowRange;
    public override void Stop() => wolf.AttackTarget = null;
}
