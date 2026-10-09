using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:rabbit")]
public sealed partial class Rabbit : FarmAnimal
{
    private int variant;
    public Rabbit()
    {
        Type = EntityType.Rabbit;
        MoveControl = new RabbitMoveControl(this);
    }
    public int Variant
    {
        get => variant;
        set
        {
            if ((value is < 0 or > 5) && value != 99)
                throw new ArgumentOutOfRangeException(nameof(value));
            variant = value;
            TryUpdateAttribute("minecraft:generic.armor", value == 99 ? 8 : 0);
            TryUpdateAttribute("minecraft:generic.attack_damage", value == 99 ? 8 : 3);
            if (value == 99 && CustomName == null)
                CustomName = "The Killer Bunny";
            SynchronizeMetadata();
        }
    }
    internal override float JumpPower => MoveControl is RabbitMoveControl { Climbing: true } ? 0.5f : 0.3f;
    protected override string? SoundName => "rabbit";
    protected override float PanicSpeed => 2.2f;
    protected override float TemptSpeed => 1;
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0, Type: Material.Carrot or Material.GoldenCarrot or Material.Dandelion };
    protected override void FinalizeSpawn() => Variant = GetNaturalVariant();
    private int GetNaturalVariant()
    {
        var point = (Vector)Position.Floor();
        var biome = (Level as AbstractLevel)?.GetLoadedChunk(point.X >> 4, point.Z >> 4)?.GetBiome(point.X, point.Y, point.Z);
        var roll = Random.Next(100);
        if (biome != null && TagsRegistry.Worldgen.Biome.SpawnsWhiteRabbits.Entries.Contains(biome.Id))
            return roll < 80 ? 1 : 3;
        if (biome != null && TagsRegistry.Worldgen.Biome.SpawnsGoldRabbits.Entries.Contains(biome.Id))
            return 4;
        return roll < 50 ? 0 : roll < 90 ? 5 : 2;
    }
    protected override IEntity CreateOffspring(Animal mate)
    {
        var child = (Rabbit)base.CreateOffspring(mate);
        child.Variant = Random.Next(20) == 0 ? GetNaturalVariant() : Random.Next(2) == 0 ? Variant : ((Rabbit)mate).Variant;
        return child;
    }
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(4, new AvoidEntityGoal(this, entity => Variant != 99 && entity is IPlayer, 8, 2.2f));
        actions.AddGoal(4, new AvoidEntityGoal(this, entity => Variant != 99 && (entity.Type is EntityType.Wolf or EntityType.Fox), 10, 2.2f));
        actions.AddGoal(4, new RabbitMeleeGoal(this));
        actions.AddGoal(5, new RabbitRaidGardenGoal(this));
        targets.AddGoal(1, new NearestAttackableTargetGoal(this, entity => Variant == 99 && entity.Uuid == LastAttacker?.Uuid));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, entity => Variant == 99 && (entity is IPlayer || entity.Type == EntityType.Wolf)));
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (!IsBaby)
        {
            DropItem(Material.RabbitHide, Random.Next(2));
            DropItem(Burning ? Material.CookedRabbit : Material.Rabbit);
            if (source is IPlayer && Random.NextSingle() < 0.1f) DropItem(Material.RabbitFoot);
        }
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.VarInt);
        writer.WriteVarInt(Variant);
    }
    protected override void WriteAdditionalSave(INbtWriter writer) => writer.WriteInt("RabbitType", Variant);
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        var saved = tag.TryGetTagValue<int>("RabbitType", out var value) ? value : 0;
        Variant = saved == 99 ? 99 : Math.Clamp(saved, 0, 5);
    }
}

internal sealed class RabbitRaidGardenGoal(Rabbit rabbit) : NavigationGoal(rabbit, 0.7f)
{
    private Vector carrot;
    private long nextSearch;
    private long timeout;
    private bool ate;
    public override bool CanUse()
    {
        if (rabbit.AiTick < nextSearch || rabbit.AttackTarget != null) return false;
        nextSearch = rabbit.AiTick + 200 + rabbit.Random.Next(200);
        var origin = (Vector)rabbit.Position.Floor();
        Vector? closest = null;
        var distance = double.MaxValue;
        for (var x = -8; x <= 8; x++)
        for (var y = -2; y <= 2; y++)
        for (var z = -8; z <= 8; z++)
        {
            var point = origin + new Vector(x, y, z);
            if (!IsMature(point) || rabbit.Terrain.GetBlock(point - new Vector(0, 1, 0))?.Material != Material.Farmland) continue;
            var squared = x * x + y * y + z * z;
            if (squared < distance) { closest = point; distance = squared; }
        }
        if (closest == null) return false;
        carrot = closest.Value;
        return true;
    }
    private bool IsMature(Vector point) => rabbit.Terrain.GetBlock(point) is { Material: Material.Carrots } block && block.GetProperty("age") == "7";
    public override void Start()
    {
        ate = false;
        timeout = rabbit.AiTick + 200;
        MoveTo(new VectorD(carrot.X + 0.5, carrot.Y, carrot.Z + 0.5));
    }
    public override bool CanContinue() => !ate && rabbit.AiTick < timeout && IsMature(carrot);
    public override async ValueTask TickAsync()
    {
        var destination = new VectorD(carrot.X + 0.5, carrot.Y, carrot.Z + 0.5);
        rabbit.LookControl.LookAt(destination);
        if ((destination - rabbit.Position).MagnitudeSquared() > 1) { MoveTo(destination); return; }
        if (rabbit.Terrain.GetBlock(carrot) is { Material: Material.Carrots } block && block.GetProperty("age") == "7")
        {
            await rabbit.Level.SetBlockAsync(carrot, block.WithProperty("age", 6), true);
            rabbit.PlayMobSound("eat");
        }
        ate = true;
    }
}

internal sealed class RabbitMeleeGoal(Rabbit rabbit) : MeleeAttackGoal(rabbit, 1.4f)
{
    public override bool CanUse() => rabbit.Variant == 99 && base.CanUse();
    public override bool CanContinue() => rabbit.Variant == 99 && base.CanContinue();
}

internal sealed class RabbitMoveControl(Rabbit rabbit) : MoveControl(rabbit)
{
    private int jumpDelay;
    internal bool Climbing { get; private set; }
    public override void MoveTo(VectorD position, float speed)
    {
        Climbing = position.Y > rabbit.Position.Y + 0.5;
        base.MoveTo(position, speed);
    }
    internal override void Tick()
    {
        base.Tick();
        if (!rabbit.MovementFlags.HasFlag(MovementFlags.OnGround) || rabbit.InWater || rabbit.InLava)
            return;
        if (jumpDelay > 0)
        {
            jumpDelay--;
            Acceleration = VectorD.Zero;
            return;
        }
        if (Acceleration.MagnitudeSquared() > 0)
        {
            rabbit.JumpControl.Jump();
            rabbit.SendEntityEvent(1);
            rabbit.PlayMobSound("jump");
            jumpDelay = rabbit.AttackTarget != null || rabbit.Burning || rabbit.AiTick - rabbit.LastHurtTick < 100 ? 1 : 10;
        }
    }
}
