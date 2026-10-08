namespace Obsidian.Entities;

[MinecraftEntity("minecraft:cow")]
public partial class Cow : FarmAnimal
{
    public Cow() => Type = EntityType.Cow;
    public int Variant { get; set; } = 1;
    protected override string? SoundName => "cow";
    protected override void FinalizeSpawn() => Variant = GetFarmVariant();

    internal override async ValueTask FeedAsync(IPlayer player, InteractionHand hand)
    {
        if (!CanInteract(player))
            return;
        var held = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (!IsBaby && held is { Count: > 0, Type: Material.Bucket })
            await GiveInteractionItemAsync(player, hand, Material.MilkBucket);
        else
            await base.FeedAsync(player, hand);
    }

    protected bool CanInteract(IPlayer player) => Alive && !IsRemoved && player.Health > 0 && player.Level == Level &&
        player.GameMode != GameMode.Spectator && IsInRange(player, 4) && CanSee(player);

    protected override IEntity CreateOffspring(Animal mate)
    {
        var child = (Cow)base.CreateOffspring(mate);
        child.Variant = Random.Next(2) == 0 ? Variant : ((Cow)mate).Variant;
        child.SynchronizeMetadata();
        return child;
    }

    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (!IsBaby)
        {
            DropItem(Material.Leather, Random.Next(3));
            DropItem(Burning ? Material.CookedBeef : Material.Beef, Random.Next(1, 4));
        }
        return default;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        if (Type == EntityType.Cow)
        {
            writer.WriteEntityMetadataType(17, EntityMetadataType.CowVariant);
            writer.WriteVarInt(Variant);
        }
    }
}
