using Obsidian.API.Inventory;

namespace Obsidian.Entities;

internal static class VillagerTrades
{
    internal static IEnumerable<TradeEntry> ForLevel(int profession, int level)
    {
        var trades = (profession, level) switch
        {
            (1, 1) => new[] { Buy(ItemsRegistry.Coal, 15), Sell(ItemsRegistry.IronHelmet, 5) },
            (1, 2) => [Buy(ItemsRegistry.IronIngot, 4), Sell(ItemsRegistry.ChainmailBoots, 1)],
            (1, 3) => [Buy(ItemsRegistry.LavaBucket, 1), Sell(ItemsRegistry.ChainmailChestplate, 4)],
            (1, 4) => [Buy(ItemsRegistry.Diamond, 1), Sell(ItemsRegistry.DiamondLeggings, 19)],
            (1, 5) => [Sell(ItemsRegistry.DiamondChestplate, 21), Sell(ItemsRegistry.DiamondHelmet, 13)],
            (2, 1) => [Buy(ItemsRegistry.Chicken, 14), Buy(ItemsRegistry.Porkchop, 7)],
            (2, 2) => [Buy(ItemsRegistry.Coal, 15), Sell(ItemsRegistry.CookedPorkchop, 1, 5)],
            (2, 3) => [Buy(ItemsRegistry.Beef, 10), Buy(ItemsRegistry.Mutton, 7)],
            (2, 4) => [Buy(ItemsRegistry.DriedKelpBlock, 10)],
            (2, 5) => [Buy(ItemsRegistry.SweetBerries, 10)],
            (3, 1) => [Buy(ItemsRegistry.Paper, 24), Sell(ItemsRegistry.Map, 7)],
            (3, 2) => [Buy(ItemsRegistry.GlassPane, 11)],
            (3, 3) => [Buy(ItemsRegistry.Compass, 1)],
            (3, 4) => [Sell(ItemsRegistry.ItemFrame, 7), Sell(ItemsRegistry.WhiteBanner, 3)],
            (3, 5) => [Sell(ItemsRegistry.GlobeBannerPattern, 8)],
            (4, 1) => [Buy(ItemsRegistry.RottenFlesh, 32), Sell(ItemsRegistry.Redstone, 1, 2)],
            (4, 2) => [Buy(ItemsRegistry.GoldIngot, 3), Sell(ItemsRegistry.LapisLazuli, 1)],
            (4, 3) => [Buy(ItemsRegistry.RabbitFoot, 2), Sell(ItemsRegistry.Glowstone, 4)],
            (4, 4) => [Buy(ItemsRegistry.GlassBottle, 9), Sell(ItemsRegistry.EnderPearl, 5)],
            (4, 5) => [Buy(ItemsRegistry.NetherWart, 22), Sell(ItemsRegistry.ExperienceBottle, 3)],
            (5, 1) => [Buy(ItemsRegistry.Wheat, 20), Buy(ItemsRegistry.Carrot, 22)],
            (5, 2) => [Buy(ItemsRegistry.Pumpkin, 6), Sell(ItemsRegistry.PumpkinPie, 1, 4)],
            (5, 3) => [Buy(ItemsRegistry.Melon, 4), Sell(ItemsRegistry.Cookie, 3, 18)],
            (5, 4) => [Sell(ItemsRegistry.Cake, 1), Sell(ItemsRegistry.Apple, 1, 4)],
            (5, 5) => [Sell(ItemsRegistry.GoldenCarrot, 3, 3), Sell(ItemsRegistry.GlisteringMelonSlice, 4, 3)],
            (6, 1) => [Buy(ItemsRegistry.String, 20), Buy(ItemsRegistry.Coal, 10)],
            (6, 2) => [Buy(ItemsRegistry.Cod, 15), Sell(ItemsRegistry.Campfire, 2)],
            (6, 3) => [Buy(ItemsRegistry.Salmon, 13), Sell(ItemsRegistry.FishingRod, 8)],
            (6, 4) => [Buy(ItemsRegistry.TropicalFish, 6)],
            (6, 5) => [Buy(ItemsRegistry.Pufferfish, 4), Buy(ItemsRegistry.OakBoat, 1)],
            (7, 1) => [Buy(ItemsRegistry.Stick, 32), Sell(ItemsRegistry.Arrow, 1, 16)],
            (7, 2) => [Buy(ItemsRegistry.Flint, 26), Sell(ItemsRegistry.Bow, 2)],
            (7, 3) => [Buy(ItemsRegistry.String, 14), Sell(ItemsRegistry.Crossbow, 3)],
            (7, 4) => [Buy(ItemsRegistry.Feather, 24), Sell(ItemsRegistry.Bow, 12)],
            (7, 5) => [Buy(ItemsRegistry.TripwireHook, 8), Sell(ItemsRegistry.Crossbow, 15)],
            (8, 1) => [Buy(ItemsRegistry.Leather, 6), Sell(ItemsRegistry.LeatherLeggings, 3)],
            (8, 2) => [Buy(ItemsRegistry.Flint, 26), Sell(ItemsRegistry.LeatherHelmet, 5)],
            (8, 3) => [Buy(ItemsRegistry.RabbitHide, 9), Sell(ItemsRegistry.LeatherChestplate, 7)],
            (8, 4) => [Buy(ItemsRegistry.TurtleScute, 4), Sell(ItemsRegistry.LeatherHorseArmor, 6)],
            (8, 5) => [Sell(ItemsRegistry.Saddle, 6)],
            (9, 1) => [Buy(ItemsRegistry.Paper, 24), Sell(ItemsRegistry.Bookshelf, 9)],
            (9, 2) => [Buy(ItemsRegistry.Book, 4), Sell(ItemsRegistry.Lantern, 1)],
            (9, 3) => [Buy(ItemsRegistry.InkSac, 5), Sell(ItemsRegistry.Glass, 1, 4)],
            (9, 4) => [Buy(ItemsRegistry.WritableBook, 2), Sell(ItemsRegistry.Clock, 5)],
            (9, 5) => [Sell(ItemsRegistry.NameTag, 20)],
            (10, 1) => [Buy(ItemsRegistry.ClayBall, 10), Sell(ItemsRegistry.Brick, 1, 10)],
            (10, 2) => [Buy(ItemsRegistry.Stone, 20), Sell(ItemsRegistry.ChiseledStoneBricks, 1, 4)],
            (10, 3) => [Buy(ItemsRegistry.Granite, 16), Sell(ItemsRegistry.PolishedAndesite, 1, 4)],
            (10, 4) => [Buy(ItemsRegistry.Quartz, 12), Sell(ItemsRegistry.WhiteTerracotta, 1)],
            (10, 5) => [Sell(ItemsRegistry.QuartzPillar, 1), Sell(ItemsRegistry.QuartzBlock, 1)],
            (12, 1) => [Buy(ItemsRegistry.WhiteWool, 18), Sell(ItemsRegistry.Shears, 2)],
            (12, 2) => [Buy(ItemsRegistry.RedDye, 12), Sell(ItemsRegistry.WhiteWool, 1)],
            (12, 3) => [Buy(ItemsRegistry.WhiteDye, 12), Sell(ItemsRegistry.WhiteBed, 3)],
            (12, 4) => [Buy(ItemsRegistry.GreenDye, 12), Sell(ItemsRegistry.WhiteBanner, 3)],
            (12, 5) => [Sell(ItemsRegistry.Painting, 2)],
            (13, 1) => [Buy(ItemsRegistry.Coal, 15), Sell(ItemsRegistry.StonePickaxe, 1)],
            (13, 2) => [Buy(ItemsRegistry.IronIngot, 4), Sell(ItemsRegistry.Bell, 36)],
            (13, 3) => [Buy(ItemsRegistry.Flint, 30), Sell(ItemsRegistry.IronPickaxe, 10)],
            (13, 4) => [Buy(ItemsRegistry.Diamond, 1), Sell(ItemsRegistry.DiamondAxe, 19)],
            (13, 5) => [Sell(ItemsRegistry.DiamondPickaxe, 18)],
            (14, 1) => [Buy(ItemsRegistry.Coal, 15), Sell(ItemsRegistry.IronAxe, 3)],
            (14, 2) => [Buy(ItemsRegistry.IronIngot, 4), Sell(ItemsRegistry.Bell, 36)],
            (14, 3) => [Buy(ItemsRegistry.Flint, 24)],
            (14, 4) => [Buy(ItemsRegistry.Diamond, 1), Sell(ItemsRegistry.DiamondAxe, 19)],
            (14, 5) => [Sell(ItemsRegistry.DiamondSword, 13)],
            _ => Array.Empty<TradeEntry>()
        };
        foreach (var trade in trades)
        {
            trade.XP = level == 1 ? 2 : level == 2 ? 10 : level == 3 ? 20 : 30;
            yield return trade;
    }
    }

    private static TradeEntry Buy(Item item, int count) =>
        MerchantOffers.Trade(item, count, ItemsRegistry.Emerald, maxUses: 16);

    private static TradeEntry Sell(Item item, int cost, int count = 1) =>
        MerchantOffers.Trade(ItemsRegistry.Emerald, cost, item, count);
}
