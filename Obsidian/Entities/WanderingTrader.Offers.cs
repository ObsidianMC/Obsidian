namespace Obsidian.Entities;

public sealed partial class WanderingTrader
{
    private ImmutableArray<TradeEntry> offers = [];
    private void CreateOffers()
    {
        TradeEntry[] buying =
        [
            MerchantOffers.Trade(Material.WaterBucket, 1, Material.Emerald, 2, 2),
            MerchantOffers.Trade(Material.MilkBucket, 1, Material.Emerald, 2, 2),
            MerchantOffers.Trade(Material.FermentedSpiderEye, 1, Material.Emerald, 3, 2),
            MerchantOffers.Trade(Material.BakedPotato, 4, Material.Emerald, 1, 2),
            MerchantOffers.Trade(Material.HayBlock, 1, Material.Emerald, 1, 2)
        ];
        TradeEntry[] rare =
        [
            MerchantOffers.Trade(Material.Emerald, 1, Material.PackedIce, 1, 6, 1),
            MerchantOffers.Trade(Material.Emerald, 6, Material.BlueIce, 1, 6, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.Gunpowder, 4, 2, 1),
            MerchantOffers.Trade(Material.Emerald, 3, Material.Podzol, 3, 6, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.AcaciaLog, 8, 4, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.BirchLog, 8, 4, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.DarkOakLog, 8, 4, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.JungleLog, 8, 4, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.OakLog, 8, 4, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.SpruceLog, 8, 4, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.CherryLog, 8, 4, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.MangroveLog, 8, 4, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.PaleOakLog, 8, 4, 1),
        ];
        TradeEntry[] common =
        [
            MerchantOffers.Trade(Material.Emerald, 3, Material.TropicalFishBucket, 1, 4, 1),
            MerchantOffers.Trade(Material.Emerald, 3, Material.PufferfishBucket, 1, 4, 1),
            MerchantOffers.Trade(Material.Emerald, 2, Material.SeaPickle, 1, 5, 1),
            MerchantOffers.Trade(Material.Emerald, 4, Material.SlimeBall, 1, 5, 1),
            MerchantOffers.Trade(Material.Emerald, 2, Material.Glowstone, 1, 5, 1),
            MerchantOffers.Trade(Material.Emerald, 5, Material.NautilusShell, 1, 5, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.Fern, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.SugarCane, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.Pumpkin, 1, 4, 1),
            MerchantOffers.Trade(Material.Emerald, 3, Material.Kelp, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 3, Material.Cactus, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.Dandelion, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.Poppy, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.BlueOrchid, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.Allium, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.AzureBluet, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.RedTulip, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.OrangeTulip, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.WhiteTulip, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.PinkTulip, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.OxeyeDaisy, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.Cornflower, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.LilyOfTheValley, 1, 7, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.OpenEyeblossom, 1, 7, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.WheatSeeds, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.BeetrootSeeds, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.PumpkinSeeds, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.MelonSeeds, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 5, Material.AcaciaSapling, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 5, Material.BirchSapling, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 5, Material.DarkOakSapling, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 5, Material.JungleSapling, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 5, Material.OakSapling, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 5, Material.SpruceSapling, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 5, Material.CherrySapling, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 5, Material.PaleOakSapling, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 5, Material.MangrovePropagule, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.RedDye, 3, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.WhiteDye, 3, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.BlueDye, 3, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.PinkDye, 3, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.BlackDye, 3, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.GreenDye, 3, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.LightGrayDye, 3, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.MagentaDye, 3, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.YellowDye, 3, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.GrayDye, 3, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.PurpleDye, 3, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.LightBlueDye, 3, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.LimeDye, 3, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.OrangeDye, 3, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.BrownDye, 3, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.CyanDye, 3, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 3, Material.BrainCoralBlock, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 3, Material.BubbleCoralBlock, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 3, Material.FireCoralBlock, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 3, Material.HornCoralBlock, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 3, Material.TubeCoralBlock, 1, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.Vine, 3, 4, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.PaleHangingMoss, 3, 4, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.BrownMushroom, 3, 4, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.RedMushroom, 3, 4, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.LilyPad, 5, 2, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.SmallDripleaf, 2, 5, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.Sand, 8, 8, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.RedSand, 4, 6, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.PointedDripstone, 2, 5, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.RootedDirt, 2, 5, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.MossBlock, 2, 5, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.PaleMossBlock, 2, 5, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.Wildflowers, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 1, Material.TallDryGrass, 1, 12, 1),
            MerchantOffers.Trade(Material.Emerald, 3, Material.FireflyBush, 1, 12, 1),
        ];
        offers = buying.OrderBy(_ => Random.Next()).Take(2)
            .Concat(rare.OrderBy(_ => Random.Next()).Take(2))
            .Concat(common.OrderBy(_ => Random.Next()).Take(5)).ToImmutableArray();
    }
}
