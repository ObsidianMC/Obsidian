namespace Obsidian.Entities;

public class ChestedHorse : AbstractHorse
{
    public bool HasChest { get; set; }
    internal override async ValueTask InteractAsync(IPlayer player, Hand hand)
    {
        if (!Alive || player.Health <= 0 || player.Level != Level || player.Gamemode == Gamemode.Spectator || !IsInRange(player, 4)) return;
        var item = hand == Hand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (HorseMask.HasFlag(HorseMask.Tamed) && !IsBaby && !HasChest && item is { Count: > 0, Type: Material.Chest })
        {
            HasChest = true;
            await ConsumeInteractionItemAsync(player, hand);
            SynchronizeMetadata();
            return;
        }
        await base.InteractAsync(player, hand);
    }
    protected override async ValueTask OnDeathAsync(IEntity source)
    {
        await base.OnDeathAsync(source);
        if (HasChest) DropItem(Material.Chest);
        if (!IsBaby) DropItem(Material.Leather, Random.Next(0, 3));
    }
    protected override void WriteAdditionalSave(Obsidian.Nbt.Interfaces.INbtWriter writer) => writer.WriteBool("ChestedHorse", HasChest);
    protected override void ReadAdditionalSave(Obsidian.Nbt.NbtCompound tag) => HasChest = tag.TryGetBool("ChestedHorse", out var chest) && chest;

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(18, EntityMetadataType.Boolean);
        writer.WriteBoolean(HasChest);
    }
}

[MinecraftEntity("minecraft:llama")]
public partial class Llama : ChestedHorse
{
    private static readonly string[] carpetColors = ["white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray",
        "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black"];
    public int Strength { get; set; }
    public Llama() => Type = EntityType.Llama;
    protected override bool UsesAi => true;
    protected override bool CanBreedHorse => true;
    protected override string? SoundName => "llama";
    internal override bool PanicsWhenHurt => false;
    protected override int MaximumTemper => 30;
    protected override bool SupportsSaddle => false;
    protected override bool IsBreedingFood(Material food) => food == Material.HayBlock;
    protected override bool CanEat(Obsidian.API.Inventory.ItemStack? item) => item is { Count: > 0, Type: Material.Wheat or Material.HayBlock };
    protected override void FinalizeSpawn()
    {
        Strength = Random.Next(1, Random.NextSingle() < 0.04f ? 6 : 4);
        Variant = (LlamaVariant)Random.Next(4);
        var health = 15 + Random.Next(8) + Random.Next(9);
        TryUpdateAttribute("minecraft:generic.max_health", health);
        Health = health;
    }
    protected override void RegisterGoals(AI.GoalSelector actions, AI.GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(2, new LlamaSpitGoal(this));
        targets.AddGoal(1, new AI.HurtByTargetGoal(this));
        targets.AddGoal(2, new AI.NearestAttackableTargetGoal(this, target => target is Wolf { Tamed: false } && IsInRange(target, 16)));
    }
    protected override IEntity CreateOffspring(Animal mate)
    {
        var child = (Llama)base.CreateOffspring(mate);
        child.Strength = Random.Next(1, Math.Max(Strength, ((Llama)mate).Strength) + 1);
        if (Random.NextSingle() < 0.03f) child.Strength = Math.Min(5, child.Strength + 1);
        child.Variant = Random.Next(2) == 0 ? Variant : ((Llama)mate).Variant;
        child.SynchronizeMetadata();
        return child;
    }
    internal override async ValueTask InteractAsync(IPlayer player, Hand hand)
    {
        if (!Alive || player.Health <= 0 || player.Level != Level || player.Gamemode == Gamemode.Spectator || !IsInRange(player, 4)) return;
        var item = hand == Hand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (HorseMask.HasFlag(HorseMask.Tamed) && item is { Count: > 0 } && item.Holder.UnlocalizedName.EndsWith("_carpet", StringComparison.Ordinal))
        {
            var color = Array.FindIndex(carpetColors, candidate => item.Holder.UnlocalizedName == $"minecraft:{candidate}_carpet");
            if (color >= 0)
            {
                var old = GetEquipment(Obsidian.API.Inventory.EquipmentSlot.Body);
                if (!old.IsAir) DropItem(old);
                CarpetColor = color;
                await ConsumeInteractionItemAsync(player, hand);
                return;
            }
        }
        await base.InteractAsync(player, hand);
    }
    protected override void WriteAdditionalSave(Obsidian.Nbt.Interfaces.INbtWriter writer)
    {
        base.WriteAdditionalSave(writer);
        writer.WriteInt("Strength", Strength);
        writer.WriteInt("Variant", (int)Variant);
    }
    protected override void ReadAdditionalSave(Obsidian.Nbt.NbtCompound tag)
    {
        base.ReadAdditionalSave(tag);
        Strength = Math.Clamp((tag.TryGetTagValue<int>("Strength", out var savedStrength) ? savedStrength : 0), 1, 5);
        Variant = (LlamaVariant)Math.Clamp((tag.TryGetTagValue<int>("Variant", out var savedVariant) ? savedVariant : 0), 0, 3);
    }

    public int CarpetColor
    {
        get => Array.FindIndex(carpetColors, color => GetEquipment(Obsidian.API.Inventory.EquipmentSlot.Body)
            .Holder.UnlocalizedName == $"minecraft:{color}_carpet");
        set
        {
            if (value is < -1 or > 15)
                throw new ArgumentOutOfRangeException(nameof(value));
            SetEquipment(Obsidian.API.Inventory.EquipmentSlot.Body, value == -1 ? Obsidian.API.Inventory.ItemStack.Air :
                ItemsRegistry.GetSingleItem($"minecraft:{carpetColors[value]}_carpet"));
        }
    }

    public LlamaVariant Variant { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(19, EntityMetadataType.VarInt);
        writer.WriteVarInt(Strength);

        writer.WriteEntityMetadataType(20, EntityMetadataType.VarInt);
        writer.WriteVarInt(Variant);
    }
}

public enum LlamaVariant : int
{
    CreamyLlama,

    WhiteLlama,

    BrownLlama,

    GrayLlama
}

[MinecraftEntity("minecraft:donkey")]
public sealed partial class Donkey : ChestedHorse
{
    public Donkey() => Type = EntityType.Donkey;
    protected override bool UsesAi => true;
    protected override bool CanBreedHorse => true;
    protected override string? SoundName => "donkey";
    protected override void FinalizeSpawn()
    {
        var health = 15 + Random.Next(8) + Random.Next(9);
        TryUpdateAttribute("minecraft:generic.max_health", health);
        TryUpdateAttribute("minecraft:generic.movement_speed", 0.175f);
        TryUpdateAttribute("minecraft:horse.jump_strength", 0.5f);
        Health = health;
    }
}

internal sealed class LlamaSpitGoal(Llama llama) : AI.NavigationGoal(llama, 1.2f)
{
    private long nextSpit;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => llama.AttackTarget is { Health: > 0 } target && llama.IsValidTarget(target);
    public override ValueTask TickAsync()
    {
        if (llama.AttackTarget is not IEntity target) return default;
        llama.LookControl.LookAt(target);
        if (!llama.IsInRange(target, 10) || !llama.CanSee(target)) MoveTo(target);
        else
        {
            Navigation.Stop();
            if (llama.AiTick >= nextSpit)
            {
                nextSpit = llama.AiTick + 40;
                llama.Level.SpawnEntity(new MobProjectile(llama, EntityType.LlamaSpit, llama.EyePosition, target.Position + new VectorF(0, target.Dimension.Height / 3, 0) - llama.EyePosition));
                llama.PlayMobSound("spit");
                if (target is not Wolf) llama.AttackTarget = null;
            }
        }
        return default;
    }
}
