namespace Obsidian.Entities;

[MinecraftEntity("minecraft:pig")]
public sealed partial class Pig : Animal
{
    private int boostTicks;
    public Pig() => Type = EntityType.Pig;

    protected override bool UsesAi => true;
    protected override string? SoundName => "pig";
    protected override int GetExperienceReward() => IsBaby ? 0 : Random.Next(1, 4);
    internal override float EyeHeight => IsBaby ? 0.425f : 0.85f;
    public int Variant { get; set; } = 1;

    protected override void FinalizeSpawn()
    {
        var (x, z) = Position.ToChunkCoord();
        if (Level is Obsidian.WorldData.AbstractLevel level && level.GetLoadedChunk(x, z) is { } chunk)
        {
            var biome = chunk.GetBiome((int)Math.Floor(Position.X), (int)Math.Floor(Position.Y), (int)Math.Floor(Position.Z));
            Variant = TagsRegistry.Worldgen.Biome.SpawnsColdVariantFarmAnimals.Entries.Contains(biome.Id) ? 0 :
                TagsRegistry.Worldgen.Biome.SpawnsWarmVariantFarmAnimals.Entries.Contains(biome.Id) ? 2 : 1;
        }
    }

    protected override IEntity CreateOffspring(Animal mate)
    {
        var child = (Pig)base.CreateOffspring(mate);
        child.Variant = Random.Next(2) == 0 ? Variant : ((Pig)mate).Variant;
        child.SynchronizeMetadata();
        return child;
    }

    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (!IsBaby)
            DropItem(Burning ? Material.CookedPorkchop : Material.Porkchop, Random.Next(1, 4));
        if (HasSaddle)
            DropItem(Material.Saddle);
        return default;
    }
    protected override void RegisterGoals(AI.GoalSelector actionGoals, AI.GoalSelector targetGoals)
    {
        actionGoals.AddGoal(0, new AI.FloatGoal(this));
        actionGoals.AddGoal(1, new AI.PanicGoal(this, 1.25f));
        actionGoals.AddGoal(3, new AI.BreedGoal(this));
        actionGoals.AddGoal(4, new AI.TemptGoal(this, item => item is { Count: > 0, Type: Material.CarrotOnAStick }));
        actionGoals.AddGoal(4, new AI.TemptGoal(this, IsFood));
        actionGoals.AddGoal(5, new AI.FollowParentGoal(this));
        actionGoals.AddGoal(6, new AI.RandomStrollGoal(this, 1));
        actionGoals.AddGoal(7, new AI.LookAtPlayerGoal(this, 6));
        actionGoals.AddGoal(8, new AI.RandomLookAroundGoal(this));
    }

    internal static bool IsFood(Obsidian.API.Inventory.ItemStack? item) => item is { Count: > 0 } &&
        item.Type is Material.Carrot or Material.Potato or Material.Beetroot;

    protected override bool CanEat(Obsidian.API.Inventory.ItemStack? item) => IsFood(item);

    internal override async ValueTask FeedAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive || IsRemoved || player.Health <= 0 || player.Level != Level || player.GameMode == GameMode.Spectator ||
            !IsInRange(player, 4) || !CanSee(player))
            return;
        var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
        var item = player.Inventory.GetItem(slot);
        if (IsFood(item))
        {
            await base.FeedAsync(player, hand);
            return;
        }
        if (HasSaddle && !player.Sneaking && player is Player rider)
        {
            Mount(rider);
            return;
        }
        if (IsBaby || HasSaddle || item is not { Count: > 0, Type: Material.Saddle })
            return;
        HasSaddle = true;
        if (player.GameMode != GameMode.Creative)
        {
            player.Inventory.RemoveItem(slot, 1);
            await player.Client.QueuePacketAsync(new Obsidian.Net.Packets.Play.Clientbound.ContainerSetSlotPacket
            {
                ContainerId = 0, Slot = (short)slot, SlotData = player.Inventory.GetItem(slot)
            });
        }
    }

    internal bool Boost(IPlayer player)
    {
        if (!ReferenceEquals(Rider, player) || !HasSaddle || TotalTimeBoost > 0)
            return false;
        TotalTimeBoost = Random.Next(841) + 140;
        boostTicks = 0;
        SynchronizeMetadata();
        return true;
    }

    protected override void TickRidden()
    {
        base.TickRidden();
        if (Rider == null || !HasSaddle ||
            !(Rider.GetHeldItem() is { Count: > 0, Type: Material.CarrotOnAStick } ||
              Rider.GetOffHandItem() is { Count: > 0, Type: Material.CarrotOnAStick }))
            return;
        Yaw = Rider.Yaw;
        Pitch = Rider.Pitch.Degrees * 0.5f;
        var boost = 1f;
        if (TotalTimeBoost > 0)
        {
            boostTicks++;
            boost = 1 + 1.15f * MathF.Sin((float)boostTicks / TotalTimeBoost * MathF.PI);
            if (boostTicks > TotalTimeBoost)
            {
                TotalTimeBoost = 0;
                SynchronizeMetadata();
            }
        }
        MoveControl.Ride(MovementSpeed * 0.225f * boost);
    }

    public bool HasSaddle
    {
        get => !GetEquipment(Obsidian.API.Inventory.EquipmentSlot.Saddle).IsAir;
        set => SetEquipment(Obsidian.API.Inventory.EquipmentSlot.Saddle,
            value ? ItemsRegistry.GetSingleItem(Material.Saddle) : Obsidian.API.Inventory.ItemStack.Air);
    }

    public int TotalTimeBoost { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(17, EntityMetadataType.VarInt);
        writer.WriteVarInt(TotalTimeBoost);
        writer.WriteEntityMetadataType(18, EntityMetadataType.PigVariant);
        writer.WriteVarInt(Variant);
    }
}
