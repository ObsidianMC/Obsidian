using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:panda")]
public sealed partial class Panda : FarmAnimal
{
    private static readonly string[] genes = ["normal", "lazy", "worried", "playful", "brown", "weak", "aggressive"];
    public Panda() => Type = EntityType.Panda;
    public byte MainGene { get; internal set; }
    public byte HiddenGene { get; internal set; }
    public int EatingTicks { get; internal set; }
    public int SneezeTicks { get; internal set; }
    public int UnhappyTicks { get; internal set; }
    public bool Rolling { get; internal set; }
    public byte ExpressedGene => MainGene is 4 or 5 && MainGene != HiddenGene ? (byte)0 : MainGene;
    protected override string? SoundName => "panda";
    internal override bool PanicsWhenHurt => IsBaby;
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0, Type: Material.Bamboo };
    internal override bool CanBreed => base.CanBreed && HasBreedingBamboo();
    private bool HasBreedingBamboo()
    {
        var origin = (Vector)Position.Floor();
        var count = 0;
        for (var x = -5; x <= 5; x++)
        for (var y = -2; y <= 2; y++)
        for (var z = -5; z <= 5; z++)
            if (Terrain.GetBlock(new Vector(origin.X + x, origin.Y + y, origin.Z + z))?.Material == Material.Bamboo && ++count >= 8)
                return true;
        return false;
    }
    private byte RandomGene()
    {
        var value = Random.Next(16);
        return value switch { 0 => 1, 1 => 2, 2 => 3, 3 => 4, 4 => 5, 5 => 6, _ => 0 };
    }
    protected override void FinalizeSpawn()
    {
        MainGene = RandomGene();
        HiddenGene = RandomGene();
        ApplyGenes();
    }
    private void ApplyGenes()
    {
        TryUpdateAttribute("minecraft:generic.max_health", ExpressedGene == 5 ? 10 : 20);
        TryUpdateAttribute("minecraft:generic.movement_speed", ExpressedGene == 1 ? 0.07f : 0.15f);
        Health = Math.Min(Health, GetAttributeValue("minecraft:generic.max_health"));
    }
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(1, new PandaRestGoal(this));
        actions.AddGoal(2, new MeleeAttackGoal(this, 1.2f));
        targets.AddGoal(1, new HurtByTargetGoal(this));
    }
    protected override IEntity CreateOffspring(Animal mate)
    {
        var child = (Panda)base.CreateOffspring(mate);
        var parent = (Panda)mate;
        child.MainGene = Random.Next(2) == 0 ? MainGene : HiddenGene;
        child.HiddenGene = Random.Next(2) == 0 ? parent.MainGene : parent.HiddenGene;
        if (Random.Next(32) == 0) child.MainGene = RandomGene();
        if (Random.Next(32) == 0) child.HiddenGene = RandomGene();
        child.ApplyGenes();
        child.SynchronizeMetadata();
        return child;
    }
    internal override async ValueTask FeedAsync(IPlayer player, Hand hand)
    {
        if (!Alive || player.Health <= 0 || player.Level != Level || player.Gamemode == Gamemode.Spectator || !IsInRange(player, 4)) return;
        var item = hand == Hand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (item is not { Count: > 0, Type: Material.Bamboo or Material.Cake }) return;
        if (item.Type == Material.Bamboo && (IsBaby || Age == 0 && HasBreedingBamboo()))
            await base.FeedAsync(player, hand);
        else
        {
            if (item.Type == Material.Bamboo && Age == 0) UnhappyTicks = 32;
            EatingTicks = 80;
            SetEquipment(EquipmentSlot.MainHand, new ItemStack(item, 1));
            await ConsumeInteractionItemAsync(player, hand);
        }
        SynchronizeMetadata();
    }
    protected internal override async ValueTask PerformMeleeAttackAsync(IEntity target)
    {
        await base.PerformMeleeAttackAsync(target);
        if (ExpressedGene != 6) AttackTarget = null;
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        if (UnhappyTicks > 0 && --UnhappyTicks == 0) SynchronizeMetadata();
        if (EatingTicks > 0)
        {
            if (--EatingTicks == 0)
            {
                Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + 2);
                SetEquipment(EquipmentSlot.MainHand, ItemStack.Air);
                SynchronizeMetadata();
            }
            else if (EatingTicks % 10 == 0) PlayMobSound("eat");
        }
        else if (!IsBaby && AttackTarget == null && Random.Next(600) == 0)
        {
            var item = GetEntitiesNear(2).OfType<ItemEntity>().FirstOrDefault(entity => entity.CanPickup &&
                entity.Item.Type is Material.Bamboo or Material.Cake);
            if (item != null)
            {
                EatingTicks = 80;
                SetEquipment(EquipmentSlot.MainHand, new ItemStack(item.Item, 1));
                if (--item.Item.Count == 0) await item.RemoveAsync();
                SynchronizeMetadata();
            }
        }
        if (SneezeTicks > 0)
        {
            if (++SneezeTicks >= 20)
            {
                SneezeTicks = 0;
                PlayMobSound("sneeze");
                if (Random.Next(700) == 0) DropItem(Material.SlimeBall);
                foreach (var panda in GetEntitiesNear(10).OfType<Panda>().Where(panda => !panda.IsBaby)) panda.JumpControl.Jump();
                SynchronizeMetadata();
            }
        }
        else if (IsBaby && Random.Next(ExpressedGene == 5 ? 500 : 6000) == 0)
        {
            SneezeTicks = 1;
            SynchronizeMetadata();
        }
        if (!InWater && EatingTicks == 0 && AttackTarget == null && ExpressedGene == 3 && Random.Next(500) == 0)
        {
            Rolling = true;
            Motion = GetLookDirection() * 0.3f + new VectorF(0, 0.4f, 0);
            SynchronizeMetadata();
        }
        else if (Rolling && MovementFlags.HasFlag(MovementFlags.OnGround)) { Rolling = false; SynchronizeMetadata(); }
    }
    internal bool Resting => EatingTicks > 0 || ExpressedGene == 2 && Level.LevelData.Thundering;
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.VarInt); writer.WriteVarInt(UnhappyTicks);
        writer.WriteEntityMetadataType(18, EntityMetadataType.VarInt); writer.WriteVarInt(SneezeTicks);
        writer.WriteEntityMetadataType(19, EntityMetadataType.VarInt); writer.WriteVarInt(EatingTicks);
        writer.WriteEntityMetadataType(20, EntityMetadataType.Byte); writer.WriteByte(MainGene);
        writer.WriteEntityMetadataType(21, EntityMetadataType.Byte); writer.WriteByte(HiddenGene);
        writer.WriteEntityMetadataType(22, EntityMetadataType.Byte); writer.WriteByte((byte)((EatingTicks > 0 ? 8 : 0) | (SneezeTicks > 0 ? 2 : 0) | (Rolling ? 4 : 0)));
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteString("MainGene", genes[MainGene]);
        writer.WriteString("HiddenGene", genes[HiddenGene]);
        writer.WriteInt("ObsidianEatingTicks", EatingTicks);
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        MainGene = (byte)Math.Max(0, Array.IndexOf(genes, (tag.TryGetTagValue<string>("MainGene", out var savedMainGene) ? savedMainGene : null)));
        HiddenGene = (byte)Math.Max(0, Array.IndexOf(genes, (tag.TryGetTagValue<string>("HiddenGene", out var savedHiddenGene) ? savedHiddenGene : null)));
        EatingTicks = Math.Clamp((tag.TryGetTagValue<int>("ObsidianEatingTicks", out var savedObsidianEatingTicks) ? savedObsidianEatingTicks : 0), 0, 80);
        ApplyGenes();
    }
}

internal sealed class PandaRestGoal(Panda panda) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Jump | GoalFlags.Look;
    public override bool CanUse() => panda.Resting;
    public override void Start() => ((Navigator)panda.Navigator!).Stop();
}
