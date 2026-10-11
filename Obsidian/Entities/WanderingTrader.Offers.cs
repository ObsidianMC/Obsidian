namespace Obsidian.Entities;

public sealed partial class WanderingTrader
{
    private ImmutableArray<TradeEntry> offers = [];
    private void CreateOffers()
    {
        TradeEntry[] buying =
        [
            MerchantOffers.Trade(ItemsRegistry.WaterBucket, 1, ItemsRegistry.Emerald, 2, 2),
            MerchantOffers.Trade(ItemsRegistry.MilkBucket, 1, ItemsRegistry.Emerald, 2, 2),
            MerchantOffers.Trade(ItemsRegistry.FermentedSpiderEye, 1, ItemsRegistry.Emerald, 3, 2),
            MerchantOffers.Trade(ItemsRegistry.BakedPotato, 4, ItemsRegistry.Emerald, 1, 2),
            MerchantOffers.Trade(ItemsRegistry.HayBlock, 1, ItemsRegistry.Emerald, 1, 2)
        ];
        TradeEntry[] rare =
        [
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.PackedIce, 1, 6, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 6, ItemsRegistry.BlueIce, 1, 6, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.Gunpowder, 4, 2, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 3, ItemsRegistry.Podzol, 3, 6, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.AcaciaLog, 8, 4, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.BirchLog, 8, 4, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.DarkOakLog, 8, 4, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.JungleLog, 8, 4, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.OakLog, 8, 4, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.SpruceLog, 8, 4, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.CherryLog, 8, 4, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.MangroveLog, 8, 4, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.PaleOakLog, 8, 4, 1),
        ];
        TradeEntry[] common =
        [
            MerchantOffers.Trade(ItemsRegistry.Emerald, 3, ItemsRegistry.TropicalFishBucket, 1, 4, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 3, ItemsRegistry.PufferfishBucket, 1, 4, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 2, ItemsRegistry.SeaPickle, 1, 5, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 4, ItemsRegistry.SlimeBall, 1, 5, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 2, ItemsRegistry.Glowstone, 1, 5, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 5, ItemsRegistry.NautilusShell, 1, 5, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.Fern, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.SugarCane, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.Pumpkin, 1, 4, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 3, ItemsRegistry.Kelp, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 3, ItemsRegistry.Cactus, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.Dandelion, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.Poppy, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.BlueOrchid, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.Allium, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.AzureBluet, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.RedTulip, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.OrangeTulip, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.WhiteTulip, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.PinkTulip, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.OxeyeDaisy, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.Cornflower, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.LilyOfTheValley, 1, 7, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.OpenEyeblossom, 1, 7, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.WheatSeeds, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.BeetrootSeeds, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.PumpkinSeeds, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.MelonSeeds, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 5, ItemsRegistry.AcaciaSapling, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 5, ItemsRegistry.BirchSapling, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 5, ItemsRegistry.DarkOakSapling, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 5, ItemsRegistry.JungleSapling, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 5, ItemsRegistry.OakSapling, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 5, ItemsRegistry.SpruceSapling, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 5, ItemsRegistry.CherrySapling, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 5, ItemsRegistry.PaleOakSapling, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 5, ItemsRegistry.MangrovePropagule, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.RedDye, 3, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.WhiteDye, 3, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.BlueDye, 3, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.PinkDye, 3, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.BlackDye, 3, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.GreenDye, 3, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.LightGrayDye, 3, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.MagentaDye, 3, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.YellowDye, 3, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.GrayDye, 3, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.PurpleDye, 3, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.LightBlueDye, 3, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.LimeDye, 3, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.OrangeDye, 3, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.BrownDye, 3, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.CyanDye, 3, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 3, ItemsRegistry.BrainCoralBlock, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 3, ItemsRegistry.BubbleCoralBlock, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 3, ItemsRegistry.FireCoralBlock, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 3, ItemsRegistry.HornCoralBlock, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 3, ItemsRegistry.TubeCoralBlock, 1, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.Vine, 3, 4, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.PaleHangingMoss, 3, 4, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.BrownMushroom, 3, 4, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.RedMushroom, 3, 4, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.LilyPad, 5, 2, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.SmallDripleaf, 2, 5, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.Sand, 8, 8, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.RedSand, 4, 6, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.PointedDripstone, 2, 5, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.RootedDirt, 2, 5, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.MossBlock, 2, 5, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.PaleMossBlock, 2, 5, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.Wildflowers, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 1, ItemsRegistry.TallDryGrass, 1, 12, 1),
            MerchantOffers.Trade(ItemsRegistry.Emerald, 3, ItemsRegistry.FireflyBush, 1, 12, 1),
        ];
        offers = buying.OrderBy(_ => Random.Next()).Take(2)
            .Concat(rare.OrderBy(_ => Random.Next()).Take(2))
            .Concat(common.OrderBy(_ => Random.Next()).Take(5)).ToImmutableArray();
    }
}
