using Obsidian.Net.Packets.Play.Clientbound;
using System.Collections.Frozen;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>ComposterBlock</c>: a composter's <c>level</c> (0 to 8) rises as compostable items are put in, each
/// one by its chance; at 7 it fills for a tick and becomes ready (8), when bone meal can be taken from it. Items go
/// in from above (<c>useItemOn</c>, or a hopper above) and bone meal comes out from below (<c>useWithoutItem</c>, or
/// a hopper below).
/// </summary>
/// <remarks>
/// The chance table is vanilla's <c>ComposterBlock.COMPOSTABLES</c>, in the same order. Dry short and tall grass are
/// not in Obsidian's item registry, so they can't be put in. Items thrown into a composter (item entities) aren't
/// handled yet.
/// </remarks>
internal static class Composters
{
    // Vanilla's ComposterBlock.READY and the level at which a composter is full and waits to become ready.
    internal const int Ready = 8;
    internal const int Full = 7;

    // Vanilla's ComposterBlock.COMPOSTABLES: the chance that an item raises the level.
    private static readonly FrozenDictionary<int, float> Chances = new Dictionary<int, float>
    {
        [ItemIds.JungleLeaves] = 0.3f,
        [ItemIds.OakLeaves] = 0.3f,
        [ItemIds.SpruceLeaves] = 0.3f,
        [ItemIds.DarkOakLeaves] = 0.3f,
        [ItemIds.PaleOakLeaves] = 0.3f,
        [ItemIds.AcaciaLeaves] = 0.3f,
        [ItemIds.CherryLeaves] = 0.3f,
        [ItemIds.BirchLeaves] = 0.3f,
        [ItemIds.AzaleaLeaves] = 0.3f,
        [ItemIds.MangroveLeaves] = 0.3f,
        [ItemIds.OakSapling] = 0.3f,
        [ItemIds.SpruceSapling] = 0.3f,
        [ItemIds.BirchSapling] = 0.3f,
        [ItemIds.JungleSapling] = 0.3f,
        [ItemIds.AcaciaSapling] = 0.3f,
        [ItemIds.CherrySapling] = 0.3f,
        [ItemIds.DarkOakSapling] = 0.3f,
        [ItemIds.PaleOakSapling] = 0.3f,
        [ItemIds.MangrovePropagule] = 0.3f,
        [ItemIds.BeetrootSeeds] = 0.3f,
        [ItemIds.DriedKelp] = 0.3f,
        [ItemIds.ShortGrass] = 0.3f,
        [ItemIds.Kelp] = 0.3f,
        [ItemIds.MelonSeeds] = 0.3f,
        [ItemIds.PumpkinSeeds] = 0.3f,
        [ItemIds.Seagrass] = 0.3f,
        [ItemIds.SweetBerries] = 0.3f,
        [ItemIds.GlowBerries] = 0.3f,
        [ItemIds.WheatSeeds] = 0.3f,
        [ItemIds.MossCarpet] = 0.3f,
        [ItemIds.PaleMossCarpet] = 0.3f,
        [ItemIds.PaleHangingMoss] = 0.3f,
        [ItemIds.PinkPetals] = 0.3f,
        [ItemIds.Wildflowers] = 0.3f,
        [ItemIds.LeafLitter] = 0.3f,
        [ItemIds.SmallDripleaf] = 0.3f,
        [ItemIds.HangingRoots] = 0.3f,
        [ItemIds.MangroveRoots] = 0.3f,
        [ItemIds.TorchflowerSeeds] = 0.3f,
        [ItemIds.PitcherPod] = 0.3f,
        [ItemIds.FireflyBush] = 0.3f,
        [ItemIds.Bush] = 0.3f,
        [ItemIds.CactusFlower] = 0.3f,
        [ItemIds.DriedKelpBlock] = 0.5f,
        [ItemIds.TallGrass] = 0.5f,
        [ItemIds.FloweringAzaleaLeaves] = 0.5f,
        [ItemIds.Cactus] = 0.5f,
        [ItemIds.SugarCane] = 0.5f,
        [ItemIds.Vine] = 0.5f,
        [ItemIds.NetherSprouts] = 0.5f,
        [ItemIds.WeepingVines] = 0.5f,
        [ItemIds.TwistingVines] = 0.5f,
        [ItemIds.MelonSlice] = 0.5f,
        [ItemIds.GlowLichen] = 0.5f,
        [ItemIds.SeaPickle] = 0.65f,
        [ItemIds.LilyPad] = 0.65f,
        [ItemIds.Pumpkin] = 0.65f,
        [ItemIds.CarvedPumpkin] = 0.65f,
        [ItemIds.Melon] = 0.65f,
        [ItemIds.Apple] = 0.65f,
        [ItemIds.Beetroot] = 0.65f,
        [ItemIds.Carrot] = 0.65f,
        [ItemIds.CocoaBeans] = 0.65f,
        [ItemIds.Potato] = 0.65f,
        [ItemIds.Wheat] = 0.65f,
        [ItemIds.BrownMushroom] = 0.65f,
        [ItemIds.RedMushroom] = 0.65f,
        [ItemIds.MushroomStem] = 0.65f,
        [ItemIds.CrimsonFungus] = 0.65f,
        [ItemIds.WarpedFungus] = 0.65f,
        [ItemIds.NetherWart] = 0.65f,
        [ItemIds.CrimsonRoots] = 0.65f,
        [ItemIds.WarpedRoots] = 0.65f,
        [ItemIds.Shroomlight] = 0.65f,
        [ItemIds.Dandelion] = 0.65f,
        [ItemIds.Poppy] = 0.65f,
        [ItemIds.BlueOrchid] = 0.65f,
        [ItemIds.Allium] = 0.65f,
        [ItemIds.AzureBluet] = 0.65f,
        [ItemIds.RedTulip] = 0.65f,
        [ItemIds.OrangeTulip] = 0.65f,
        [ItemIds.WhiteTulip] = 0.65f,
        [ItemIds.PinkTulip] = 0.65f,
        [ItemIds.OxeyeDaisy] = 0.65f,
        [ItemIds.Cornflower] = 0.65f,
        [ItemIds.LilyOfTheValley] = 0.65f,
        [ItemIds.WitherRose] = 0.65f,
        [ItemIds.OpenEyeblossom] = 0.65f,
        [ItemIds.ClosedEyeblossom] = 0.65f,
        [ItemIds.Fern] = 0.65f,
        [ItemIds.Sunflower] = 0.65f,
        [ItemIds.Lilac] = 0.65f,
        [ItemIds.RoseBush] = 0.65f,
        [ItemIds.Peony] = 0.65f,
        [ItemIds.LargeFern] = 0.65f,
        [ItemIds.SporeBlossom] = 0.65f,
        [ItemIds.Azalea] = 0.65f,
        [ItemIds.MossBlock] = 0.65f,
        [ItemIds.PaleMossBlock] = 0.65f,
        [ItemIds.BigDripleaf] = 0.65f,
        [ItemIds.HayBlock] = 0.85f,
        [ItemIds.BrownMushroomBlock] = 0.85f,
        [ItemIds.RedMushroomBlock] = 0.85f,
        [ItemIds.NetherWartBlock] = 0.85f,
        [ItemIds.WarpedWartBlock] = 0.85f,
        [ItemIds.FloweringAzalea] = 0.85f,
        [ItemIds.Bread] = 0.85f,
        [ItemIds.BakedPotato] = 0.85f,
        [ItemIds.Cookie] = 0.85f,
        [ItemIds.Torchflower] = 0.85f,
        [ItemIds.PitcherPlant] = 0.85f,
        [ItemIds.Cake] = 1.0f,
        [ItemIds.PumpkinPie] = 1.0f,
    }.ToFrozenDictionary();

    /// <summary>Vanilla's <c>ComposterBlock.tick</c> delay for a full composter to become ready.</summary>
    internal const int ReadyDelay = 20;

    /// <summary>The chance that <paramref name="itemId"/> raises a composter's level, or null if it's not compostable.</summary>
    internal static float? ChanceOf(int itemId) => Chances.TryGetValue(itemId, out var chance) ? chance : null;

    /// <summary>The composter's level, or -1 if the block isn't one.</summary>
    internal static int LevelOf(IBlock block) =>
        block.Is(BlockIds.Composter) && int.TryParse(block.GetProperty("level"), out var level) ? level : -1;

    /// <summary>
    /// Vanilla's <c>ComposterBlock.addItem</c>: the item is consumed whether or not it raises the level. An empty
    /// composter always rises for a compostable item; otherwise the level rises only if the roll passes its chance.
    /// The fill event reports whether it rose.
    /// </summary>
    internal static IBlock Filled(IBlock block, float chance, float roll, out bool rose)
    {
        var level = LevelOf(block);
        rose = (level == 0 && chance > 0) || roll < chance;
        return rose ? block.WithProperty("level", (level + 1).ToString()) : block;
    }

    /// <summary>Plays a composter sound to the players in range, from its block.</summary>
    internal static void PlaySound(AbstractLevel level, Vector position, string sound)
    {
        level.PacketBroadcaster.BroadcastToLevelInRange(level, position, new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(position.X + 0.5, position.Y + 0.5, position.Z + 0.5),
            Category = SoundCategory.Blocks,
            Volume = 1,
            Pitch = 1,
            Seed = Random.Shared.NextInt64()
        });
    }
}
