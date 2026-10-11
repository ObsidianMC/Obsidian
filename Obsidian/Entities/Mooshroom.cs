using Obsidian.API.Inventory;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:mooshroom")]
public sealed partial class Mooshroom : Cow
{
    public Mooshroom() => Type = EntityType.Mooshroom;
    public bool Brown { get; set; }
    internal Obsidian.API.Effects.SuspiciousStewEffect? StewEffect { get; set; }

    internal override async ValueTask FeedAsync(IPlayer player, InteractionHand hand)
    {
        if (!CanInteract(player))
            return;
        var held = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (!IsBaby && held is { Count: > 0, Holder.Id: ItemIds.Bowl })
        {
            var stew = StewEffect == null ? ItemsRegistry.MushroomStew : ItemsRegistry.SuspiciousStew;
            var item = ItemsRegistry.GetSingleItem(stew);
            if (StewEffect is { } effect)
            {
                var component = Obsidian.API.Inventory.DataComponents.ComponentBuilder.SuspiciousStewEffects;
                component.Value = [effect];
                item[DataComponentType.SuspiciousStewEffects] = component;
                StewEffect = null;
            }
            await GiveInteractionItemAsync(player, hand, item);
        }
        else if (Brown && held is { Count: > 0, Holder: Item flower } && GetFlowerEffect(flower) is { } effect)
        {
            if (StewEffect == null)
            {
                StewEffect = effect;
                if (player.GameMode != GameMode.Creative)
                {
                    var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
                    player.Inventory.RemoveItem(slot, 1);
                    await player.SendInventorySlotAsync(slot);
                }
            }
        }
        else if (!IsBaby && held is { Count: > 0, Holder.Id: ItemIds.Shears })
        {
            DropItem(Brown ? ItemsRegistry.BrownMushroom : ItemsRegistry.RedMushroom, 5);
            await DamageInteractionToolAsync(player, hand);
            await ConvertToAsync(EntityType.Cow);
        }
        else
            await base.FeedAsync(player, hand);
    }

    private static Obsidian.API.Effects.SuspiciousStewEffect? GetFlowerEffect(Item flower)
    {
        (PotionEffect Effect, int Duration)? value = flower.Id switch
        {
            ItemIds.Dandelion or ItemIds.BlueOrchid => (PotionEffect.Saturation, 7),
            ItemIds.Poppy or ItemIds.Torchflower => (PotionEffect.NightVision, 100),
            ItemIds.Allium => (PotionEffect.FireResistance, 80),
            ItemIds.AzureBluet => (PotionEffect.Blindness, 160),
            ItemIds.RedTulip
                or ItemIds.OrangeTulip
                or ItemIds.WhiteTulip
                or ItemIds.PinkTulip => (PotionEffect.Weakness, 180),
            ItemIds.OxeyeDaisy => (PotionEffect.Regeneration, 160),
            ItemIds.Cornflower => (PotionEffect.JumpBoost, 120),
            ItemIds.LilyOfTheValley => (PotionEffect.Poison, 240),
            ItemIds.WitherRose => (PotionEffect.Wither, 160),
            ItemIds.OpenEyeblossom => (PotionEffect.Blindness, 220),
            ItemIds.ClosedEyeblossom => (PotionEffect.Nausea, 140),
            _ => null
        };
        return value is { } effect ? new() { EffectId = (int)effect.Effect - 1, Duration = effect.Duration } : null;
    }

    protected override IEntity CreateOffspring(Animal mate)
    {
        var child = (Mooshroom)base.CreateOffspring(mate);
        var other = (Mooshroom)mate;
        child.Brown = Brown == other.Brown ? (Random.Next(1024) == 0 ? !Brown : Brown) : Random.Next(2) == 0;
        child.SynchronizeMetadata();
        return child;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.VarInt);
        writer.WriteVarInt(Brown ? 1 : 0);
    }
}
