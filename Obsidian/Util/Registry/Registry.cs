using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Items;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.Util.Extensions;
using Obsidian.Util.Registry.Codecs;
using Obsidian.Util.Registry.Codecs.Biomes;
using Obsidian.Util.Registry.Codecs.Dimensions;
using Obsidian.Util.Registry.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Util.Registry
{
    public class Registry
    {
        internal static ILogger Logger { get; set; }

        public static Dictionary<Materials, Item> Items = new Dictionary<Materials, Item>();
        public static Block[] Blocks = new Block[763];
        public static Dictionary<Biomes, int> Biomes = new Dictionary<Biomes, int>();

        public static Dictionary<string, List<Tag>> Tags = new Dictionary<string, List<Tag>>();

        public static MatchTarget[] StateToMatch = new MatchTarget[17112];
        public static short[] NumericToBase = new short[763];

        internal static CodecCollection<int, DimensionCodec> DefaultDimensions { get; } = new CodecCollection<int, DimensionCodec>("minecraft:dimension_type");

        internal static CodecCollection<string, BiomeCodec> DefaultBiomes { get; } = new CodecCollection<string, BiomeCodec>("minecraft:worldgen/biome");

        public static async Task RegisterBlocksAsync()
        {
            var file = new FileInfo("Assets/blocks.json");

            if (file.Exists)
            {
                using var fs = file.OpenRead();
                using var read = new StreamReader(fs, new UTF8Encoding(false));

                string json = await read.ReadToEndAsync();

                int registered = 0;

                var type = JObject.Parse(json);

                using (var enumerator = type.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        var (blockName, token) = enumerator.Current;

                        var name = blockName.Split(":")[1];

                        var states = JsonConvert.DeserializeObject<BlockJson>(token.ToString(), Globals.JsonSettings);

                        if (!Enum.TryParse(name.Replace("_", ""), true, out Materials material))
                            continue;

                        if (states.States.Length <= 0)
                            continue;

                        int id = 0;
                        foreach (var state in states.States)
                            id = state.Default ? state.Id : states.States.First().Id;

                        var baseId = (short)states.States.Min(state => state.Id);
                        NumericToBase[(int)material] = baseId;

                        Logger.LogDebug($"Registered block: {material} with id: {id}");

                        switch (material)
                        {
                            case Materials.GrassBlock:
                                Blocks[(int)material] = new BlockGrass(blockName, id, material);
                                break;
                            case Materials.Podzol:
                                Blocks[(int)material] = new BlockDirtSnow(blockName, id, material);
                                break;
                            case Materials.OakSapling:
                                Blocks[(int)material] = new BlockSapling(blockName, id, material);
                                break;
                            case Materials.SpruceSapling:
                                Blocks[(int)material] = new BlockSapling(blockName, id, material);
                                break;
                            case Materials.BirchSapling:
                                Blocks[(int)material] = new BlockSapling(blockName, id, material);
                                break;
                            case Materials.JungleSapling:
                                Blocks[(int)material] = new BlockSapling(blockName, id, material);
                                break;
                            case Materials.AcaciaSapling:
                                Blocks[(int)material] = new BlockSapling(blockName, id, material);
                                break;
                            case Materials.DarkOakSapling:
                                Blocks[(int)material] = new BlockSapling(blockName, id, material);
                                break;
                            case Materials.Water:
                                Blocks[(int)material] = new BlockFluid(blockName, id, material);
                                break;
                            case Materials.Lava:
                                Blocks[(int)material] = new BlockFluid(blockName, id, material);
                                break;
                            case Materials.OakLog:
                                Blocks[(int)material] = new BlockLog(blockName, id, material);
                                break;
                            case Materials.SpruceLog:
                                Blocks[(int)material] = new BlockLog(blockName, id, material);
                                break;
                            case Materials.BirchLog:
                                Blocks[(int)material] = new BlockLog(blockName, id, material);
                                break;
                            case Materials.JungleLog:
                                Blocks[(int)material] = new BlockLog(blockName, id, material);
                                break;
                            case Materials.AcaciaLog:
                                Blocks[(int)material] = new BlockLog(blockName, id, material);
                                break;
                            case Materials.DarkOakLog:
                                Blocks[(int)material] = new BlockLog(blockName, id, material);
                                break;
                            case Materials.StrippedOakLog:
                                Blocks[(int)material] = new BlockLog(blockName, id, material);
                                break;
                            case Materials.StrippedSpruceLog:
                                Blocks[(int)material] = new BlockLog(blockName, id, material);
                                break;
                            case Materials.StrippedBirchLog:
                                Blocks[(int)material] = new BlockLog(blockName, id, material);
                                break;
                            case Materials.StrippedJungleLog:
                                Blocks[(int)material] = new BlockLog(blockName, id, material);
                                break;
                            case Materials.StrippedAcaciaLog:
                                Blocks[(int)material] = new BlockLog(blockName, id, material);
                                break;
                            case Materials.StrippedDarkOakLog:
                                Blocks[(int)material] = new BlockLog(blockName, id, material);
                                break;
                            case Materials.OakWood:
                                Blocks[(int)material] = new BlockRotatable(blockName, id, material);
                                break;
                            case Materials.SpruceWood:
                                Blocks[(int)material] = new BlockRotatable(blockName, id, material);
                                break;
                            case Materials.BirchWood:
                                Blocks[(int)material] = new BlockRotatable(blockName, id, material);
                                break;
                            case Materials.JungleWood:
                                Blocks[(int)material] = new BlockRotatable(blockName, id, material);
                                break;
                            case Materials.AcaciaWood:
                                Blocks[(int)material] = new BlockRotatable(blockName, id, material);
                                break;
                            case Materials.DarkOakWood:
                                Blocks[(int)material] = new BlockRotatable(blockName, id, material);
                                break;
                            case Materials.StrippedOakWood:
                                Blocks[(int)material] = new BlockRotatable(blockName, id, material);
                                break;
                            case Materials.StrippedSpruceWood:
                                Blocks[(int)material] = new BlockRotatable(blockName, id, material);
                                break;
                            case Materials.StrippedBirchWood:
                                Blocks[(int)material] = new BlockRotatable(blockName, id, material);
                                break;
                            case Materials.StrippedJungleWood:
                                Blocks[(int)material] = new BlockRotatable(blockName, id, material);
                                break;
                            case Materials.StrippedAcaciaWood:
                                Blocks[(int)material] = new BlockRotatable(blockName, id, material);
                                break;
                            case Materials.StrippedDarkOakWood:
                                Blocks[(int)material] = new BlockRotatable(blockName, id, material);
                                break;
                            case Materials.OakLeaves:
                                Blocks[(int)material] = new BlockLeaves(blockName, id, material);
                                break;
                            case Materials.SpruceLeaves:
                                Blocks[(int)material] = new BlockLeaves(blockName, id, material);
                                break;
                            case Materials.BirchLeaves:
                                Blocks[(int)material] = new BlockLeaves(blockName, id, material);
                                break;
                            case Materials.JungleLeaves:
                                Blocks[(int)material] = new BlockLeaves(blockName, id, material);
                                break;
                            case Materials.AcaciaLeaves:
                                Blocks[(int)material] = new BlockLeaves(blockName, id, material);
                                break;
                            case Materials.DarkOakLeaves:
                                Blocks[(int)material] = new BlockLeaves(blockName, id, material);
                                break;
                            case Materials.Sponge:
                                Blocks[(int)material] = new BlockSponge(blockName, id, material);
                                break;
                            case Materials.WetSponge:
                                Blocks[(int)material] = new BlockSponge(blockName, id, material);
                                break;
                            case Materials.Dispenser:
                                Blocks[(int)material] = new BlockDispenser(blockName, id, material);
                                break;
                            case Materials.NoteBlock:
                                Blocks[(int)material] = new BlockNote(blockName, id, material);
                                break;
                            case Materials.WhiteBed:
                                Blocks[(int)material] = new BlockBed(blockName, id, material);
                                break;
                            case Materials.OrangeBed:
                                Blocks[(int)material] = new BlockBed(blockName, id, material);
                                break;
                            case Materials.MagentaBed:
                                Blocks[(int)material] = new BlockBed(blockName, id, material);
                                break;
                            case Materials.LightBlueBed:
                                Blocks[(int)material] = new BlockBed(blockName, id, material);
                                break;
                            case Materials.YellowBed:
                                Blocks[(int)material] = new BlockBed(blockName, id, material);
                                break;
                            case Materials.LimeBed:
                                Blocks[(int)material] = new BlockBed(blockName, id, material);
                                break;
                            case Materials.PinkBed:
                                Blocks[(int)material] = new BlockBed(blockName, id, material);
                                break;
                            case Materials.GrayBed:
                                Blocks[(int)material] = new BlockBed(blockName, id, material);
                                break;
                            case Materials.LightGrayBed:
                                Blocks[(int)material] = new BlockBed(blockName, id, material);
                                break;
                            case Materials.CyanBed:
                                Blocks[(int)material] = new BlockBed(blockName, id, material);
                                break;
                            case Materials.PurpleBed:
                                Blocks[(int)material] = new BlockBed(blockName, id, material);
                                break;
                            case Materials.BlueBed:
                                Blocks[(int)material] = new BlockBed(blockName, id, material);
                                break;
                            case Materials.BrownBed:
                                Blocks[(int)material] = new BlockBed(blockName, id, material);
                                break;
                            case Materials.GreenBed:
                                Blocks[(int)material] = new BlockBed(blockName, id, material);
                                break;
                            case Materials.RedBed:
                                Blocks[(int)material] = new BlockBed(blockName, id, material);
                                break;
                            case Materials.BlackBed:
                                Blocks[(int)material] = new BlockBed(blockName, id, material);
                                break;
                            case Materials.PoweredRail:
                                Blocks[(int)material] = new BlockPoweredRail(blockName, id, material);
                                break;
                            case Materials.DetectorRail:
                                Blocks[(int)material] = new BlockMinecartDetector(blockName, id, material);
                                break;
                            case Materials.StickyPiston:
                                Blocks[(int)material] = new BlockPiston(blockName, id, material);
                                break;
                            case Materials.Cobweb:
                                Blocks[(int)material] = new BlockWeb(blockName, id, material);
                                break;
                            case Materials.Grass:
                                Blocks[(int)material] = new BlockLongGrass(blockName, id, material);
                                break;
                            case Materials.Fern:
                                Blocks[(int)material] = new BlockLongGrass(blockName, id, material);
                                break;
                            case Materials.DeadBush:
                                Blocks[(int)material] = new BlockDeadBush(blockName, id, material);
                                break;
                            case Materials.Seagrass:
                                Blocks[(int)material] = new BlockSeaGrass(blockName, id, material);
                                break;
                            case Materials.TallSeagrass:
                                Blocks[(int)material] = new BlockTallSeaGrass(blockName, id, material);
                                break;
                            case Materials.Piston:
                                Blocks[(int)material] = new BlockPiston(blockName, id, material);
                                break;
                            case Materials.PistonHead:
                                Blocks[(int)material] = new BlockPistonExtension(blockName, id, material);
                                break;
                            case Materials.MovingPiston:
                                Blocks[(int)material] = new BlockPiston(blockName, id, material);
                                break;
                            case Materials.Dandelion:
                                Blocks[(int)material] = new BlockFlower(blockName, id, material);
                                break;
                            case Materials.Poppy:
                                Blocks[(int)material] = new BlockFlower(blockName, id, material);
                                break;
                            case Materials.BlueOrchid:
                                Blocks[(int)material] = new BlockFlower(blockName, id, material);
                                break;
                            case Materials.Allium:
                                Blocks[(int)material] = new BlockFlower(blockName, id, material);
                                break;
                            case Materials.AzureBluet:
                                Blocks[(int)material] = new BlockFlower(blockName, id, material);
                                break;
                            case Materials.RedTulip:
                                Blocks[(int)material] = new BlockFlower(blockName, id, material);
                                break;
                            case Materials.OrangeTulip:
                                Blocks[(int)material] = new BlockFlower(blockName, id, material);
                                break;
                            case Materials.WhiteTulip:
                                Blocks[(int)material] = new BlockFlower(blockName, id, material);
                                break;
                            case Materials.PinkTulip:
                                Blocks[(int)material] = new BlockFlower(blockName, id, material);
                                break;
                            case Materials.OxeyeDaisy:
                                Blocks[(int)material] = new BlockFlower(blockName, id, material);
                                break;
                            case Materials.BrownMushroom:
                                Blocks[(int)material] = new BlockMushroom(blockName, id, material);
                                break;
                            case Materials.RedMushroom:
                                Blocks[(int)material] = new BlockMushroom(blockName, id, material);
                                break;
                            case Materials.Tnt:
                                Blocks[(int)material] = new BlockTnt(blockName, id);
                                break;
                            case Materials.Torch:
                                Blocks[(int)material] = new BlockTorch(blockName, id, material);
                                break;
                            case Materials.WallTorch:
                                Blocks[(int)material] = new BlockWallTorch(blockName, id);
                                break;
                            case Materials.Fire:
                                Blocks[(int)material] = new BlockFire(blockName, id);
                                break;
                            case Materials.Spawner:
                                Blocks[(int)material] = new BlockMobSpawner(blockName, id);
                                break;
                            case Materials.OakStairs:
                                Blocks[(int)material] = new BlockStairs(blockName, id, material);
                                break;
                            case Materials.Chest:
                                Blocks[(int)material] = new Chest(blockName, id, material);
                                break;
                            case Materials.RedstoneWire:
                                Blocks[(int)material] = new BlockRedstoneWire(blockName, id);
                                break;
                            case Materials.CraftingTable:
                                Blocks[(int)material] = new BlockWorkbench(blockName, id);
                                break;
                            case Materials.Wheat:
                                Blocks[(int)material] = new BlockCrops(blockName, id, material);
                                break;
                            case Materials.Farmland:
                                Blocks[(int)material] = new BlockSoil(blockName, id);
                                break;
                            case Materials.Furnace:
                                Blocks[(int)material] = new BlockFurnace(blockName, id);
                                break;
                            case Materials.AcaciaSign://TODO signs
                                Blocks[(int)material] = new BlockFloorSign(blockName, id, material);
                                break;
                            case Materials.BirchSign:
                                Blocks[(int)material] = new BlockFloorSign(blockName, id, material);
                                break;
                            case Materials.DarkOakSign:
                                Blocks[(int)material] = new BlockFloorSign(blockName, id, material);
                                break;
                            case Materials.JungleSign:
                                Blocks[(int)material] = new BlockFloorSign(blockName, id, material);
                                break;
                            case Materials.OakSign:
                                Blocks[(int)material] = new BlockFloorSign(blockName, id, material);
                                break;
                            case Materials.SpruceSign:
                                Blocks[(int)material] = new BlockFloorSign(blockName, id, material);
                                break;
                            case Materials.OakDoor:
                                Blocks[(int)material] = new BlockDoor(blockName, id, material);
                                break;
                            case Materials.Ladder:
                                Blocks[(int)material] = new BlockLadder(blockName, id);
                                break;
                            case Materials.Rail:
                                Blocks[(int)material] = new BlockMinecartTrack(blockName, id);
                                break;
                            case Materials.CobblestoneStairs:
                                Blocks[(int)material] = new BlockStairs(blockName, id, material);
                                break;
                            case Materials.AcaciaWallSign://TODO different wall signs
                                Blocks[(int)material] = new BlockWallSign(blockName, id, material);
                                break;
                            case Materials.BirchWallSign:
                                Blocks[(int)material] = new BlockWallSign(blockName, id, material);
                                break;
                            case Materials.DarkOakWallSign:
                                Blocks[(int)material] = new BlockWallSign(blockName, id, material);
                                break;
                            case Materials.JungleWallSign:
                                Blocks[(int)material] = new BlockWallSign(blockName, id, material);
                                break;
                            case Materials.OakWallSign:
                                Blocks[(int)material] = new BlockWallSign(blockName, id, material);
                                break;
                            case Materials.SpruceWallSign:
                                Blocks[(int)material] = new BlockWallSign(blockName, id, material);
                                break;
                            case Materials.Lever:
                                Blocks[(int)material] = new BlockLever(blockName, id);
                                break;
                            case Materials.StonePressurePlate:
                                Blocks[(int)material] = new BlockPressurePlate(blockName, id, material);
                                break;
                            case Materials.IronDoor:
                                Blocks[(int)material] = new BlockDoor(blockName, id, material);
                                break;
                            case Materials.OakPressurePlate:
                                Blocks[(int)material] = new BlockPressurePlate(blockName, id, material);
                                break;
                            case Materials.SprucePressurePlate:
                                Blocks[(int)material] = new BlockPressurePlate(blockName, id, material);
                                break;
                            case Materials.BirchPressurePlate:
                                Blocks[(int)material] = new BlockPressurePlate(blockName, id, material);
                                break;
                            case Materials.JunglePressurePlate:
                                Blocks[(int)material] = new BlockPressurePlate(blockName, id, material);
                                break;
                            case Materials.AcaciaPressurePlate:
                                Blocks[(int)material] = new BlockPressurePlate(blockName, id, material);
                                break;
                            case Materials.DarkOakPressurePlate:
                                Blocks[(int)material] = new BlockPressurePlate(blockName, id, material);
                                break;
                            case Materials.RedstoneTorch:
                                Blocks[(int)material] = new BlockRedstoneTorch(blockName, id, material);
                                break;
                            case Materials.RedstoneWallTorch:
                                Blocks[(int)material] = new BlockRedstoneWallTorch(blockName, id);
                                break;
                            case Materials.StoneButton:
                                Blocks[(int)material] = new BlockButton(blockName, id, material);
                                break;
                            case Materials.Snow:
                                Blocks[(int)material] = new BlockSnow(blockName, id);
                                break;
                            case Materials.Ice:
                                Blocks[(int)material] = new BlockIce(blockName, id, material);
                                break;
                            case Materials.Cactus:
                                Blocks[(int)material] = new BlockCactus(blockName, id);
                                break;
                            case Materials.Clay:
                                Blocks[(int)material] = new Block(blockName, id, material);
                                break;
                            case Materials.SugarCane:
                                Blocks[(int)material] = new BlockReed(blockName, id, material);
                                break;
                            case Materials.Jukebox:
                                Blocks[(int)material] = new BlockJukebox(blockName, id);
                                break;
                            case Materials.OakFence:
                                Blocks[(int)material] = new BlockFence(blockName, id, material);
                                break;
                            case Materials.Pumpkin:
                                Blocks[(int)material] = new BlockPumpkin(blockName, id);
                                break;
                            case Materials.SoulSand:
                                Blocks[(int)material] = new BlockSlowSand(blockName, id);
                                break;
                            case Materials.NetherPortal:
                                Blocks[(int)material] = new BlockPortal(blockName, id);
                                break;
                            case Materials.CarvedPumpkin:
                                Blocks[(int)material] = new BlockPumpkinCarved(blockName, id, material);
                                break;
                            case Materials.JackOLantern:
                                Blocks[(int)material] = new BlockPumpkinCarved(blockName, id, material);
                                break;
                            case Materials.Cake:
                                Blocks[(int)material] = new BlockCake(blockName, id);
                                break;
                            case Materials.Repeater:
                                Blocks[(int)material] = new BlockRepeater(blockName, id);
                                break;
                            case Materials.WhiteStainedGlass:
                                Blocks[(int)material] = new BlockStainedGlass(blockName, id, material);
                                break;
                            case Materials.OrangeStainedGlass:
                                Blocks[(int)material] = new BlockStainedGlass(blockName, id, material);
                                break;
                            case Materials.MagentaStainedGlass:
                                Blocks[(int)material] = new BlockStainedGlass(blockName, id, material);
                                break;
                            case Materials.LightBlueStainedGlass:
                                Blocks[(int)material] = new BlockStainedGlass(blockName, id, material);
                                break;
                            case Materials.YellowStainedGlass:
                                Blocks[(int)material] = new BlockStainedGlass(blockName, id, material);
                                break;
                            case Materials.LimeStainedGlass:
                                Blocks[(int)material] = new BlockStainedGlass(blockName, id, material);
                                break;
                            case Materials.PinkStainedGlass:
                                Blocks[(int)material] = new BlockStainedGlass(blockName, id, material);
                                break;
                            case Materials.GrayStainedGlass:
                                Blocks[(int)material] = new BlockStainedGlass(blockName, id, material);
                                break;
                            case Materials.LightGrayStainedGlass:
                                Blocks[(int)material] = new BlockStainedGlass(blockName, id, material);
                                break;
                            case Materials.CyanStainedGlass:
                                Blocks[(int)material] = new BlockStainedGlass(blockName, id, material);
                                break;
                            case Materials.PurpleStainedGlass:
                                Blocks[(int)material] = new BlockStainedGlass(blockName, id, material);
                                break;
                            case Materials.BlueStainedGlass:
                                Blocks[(int)material] = new BlockStainedGlass(blockName, id, material);
                                break;
                            case Materials.BrownStainedGlass:
                                Blocks[(int)material] = new BlockStainedGlass(blockName, id, material);
                                break;
                            case Materials.GreenStainedGlass:
                                Blocks[(int)material] = new BlockStainedGlass(blockName, id, material);
                                break;
                            case Materials.RedStainedGlass:
                                Blocks[(int)material] = new BlockStainedGlass(blockName, id, material);
                                break;
                            case Materials.BlackStainedGlass:
                                Blocks[(int)material] = new BlockStainedGlass(blockName, id, material);
                                break;
                            case Materials.OakTrapdoor:
                                Blocks[(int)material] = new BlockTrapdoor(blockName, id, material);
                                break;
                            case Materials.SpruceTrapdoor:
                                Blocks[(int)material] = new BlockTrapdoor(blockName, id, material);
                                break;
                            case Materials.BirchTrapdoor:
                                Blocks[(int)material] = new BlockTrapdoor(blockName, id, material);
                                break;
                            case Materials.JungleTrapdoor:
                                Blocks[(int)material] = new BlockTrapdoor(blockName, id, material);
                                break;
                            case Materials.AcaciaTrapdoor:
                                Blocks[(int)material] = new BlockTrapdoor(blockName, id, material);
                                break;
                            case Materials.DarkOakTrapdoor:
                                Blocks[(int)material] = new BlockTrapdoor(blockName, id, material);
                                break;
                            case Materials.InfestedStone:
                                Blocks[(int)material] = new BlockMonsterEggs(blockName, id, material);
                                break;
                            case Materials.InfestedCobblestone:
                                Blocks[(int)material] = new BlockMonsterEggs(blockName, id, material);
                                break;
                            case Materials.InfestedStoneBricks:
                                Blocks[(int)material] = new BlockMonsterEggs(blockName, id, material);
                                break;
                            case Materials.InfestedMossyStoneBricks:
                                Blocks[(int)material] = new BlockMonsterEggs(blockName, id, material);
                                break;
                            case Materials.InfestedCrackedStoneBricks:
                                Blocks[(int)material] = new BlockMonsterEggs(blockName, id, material);
                                break;
                            case Materials.InfestedChiseledStoneBricks:
                                Blocks[(int)material] = new BlockMonsterEggs(blockName, id, material);
                                break;
                            case Materials.BrownMushroomBlock:
                                Blocks[(int)material] = new BlockHugeMushroom(blockName, id, material);
                                break;
                            case Materials.RedMushroomBlock:
                                Blocks[(int)material] = new BlockHugeMushroom(blockName, id, material);
                                break;
                            case Materials.MushroomStem:
                                Blocks[(int)material] = new BlockHugeMushroom(blockName, id, material);
                                break;
                            case Materials.IronBars:
                                Blocks[(int)material] = new BlockIronBars(blockName, id);
                                break;
                            case Materials.GlassPane:
                                Blocks[(int)material] = new Block(blockName, id, material);
                                break;
                            case Materials.Melon:
                                Blocks[(int)material] = new BlockMelon(blockName, id);
                                break;
                            case Materials.AttachedPumpkinStem:
                                Blocks[(int)material] = new BlockStemAttached(blockName, id, material);
                                break;
                            case Materials.AttachedMelonStem:
                                Blocks[(int)material] = new BlockStemAttached(blockName, id, material);
                                break;
                            case Materials.PumpkinStem:
                                Blocks[(int)material] = new BlockStem(blockName, id, material);
                                break;
                            case Materials.MelonStem:
                                Blocks[(int)material] = new BlockStem(blockName, id, material);
                                break;
                            case Materials.Vine:
                                Blocks[(int)material] = new BlockVine(blockName, id);
                                break;
                            case Materials.OakFenceGate:
                                Blocks[(int)material] = new BlockFenceGate(blockName, id, material);
                                break;
                            case Materials.BrickStairs:
                                Blocks[(int)material] = new BlockStairs(blockName, id, material);
                                break;
                            case Materials.StoneBrickStairs:
                                Blocks[(int)material] = new BlockStairs(blockName, id, material);
                                break;
                            case Materials.Mycelium:
                                Blocks[(int)material] = new BlockMycel(blockName, id);
                                break;
                            case Materials.LilyPad:
                                Blocks[(int)material] = new BlockWaterLily(blockName, id);
                                break;
                            case Materials.NetherBrickFence:
                                Blocks[(int)material] = new BlockFence(blockName, id, material);
                                break;
                            case Materials.NetherBrickStairs:
                                Blocks[(int)material] = new BlockStairs(blockName, id, material);
                                break;
                            case Materials.NetherWart:
                                Blocks[(int)material] = new BlockNetherWart(blockName, id);
                                break;
                            case Materials.EnchantingTable:
                                Blocks[(int)material] = new BlockEnchantingTable(blockName, id);
                                break;
                            case Materials.BrewingStand:
                                Blocks[(int)material] = new BlockBrewingStand(blockName, id);
                                break;
                            case Materials.Cauldron:
                                Blocks[(int)material] = new BlockCauldron(blockName, id);
                                break;
                            default:
                                Blocks[(int)material] = new Block(blockName, id, material);
                                break;
                        }

                        foreach (var state in states.States)
                        {
                            StateToMatch[state.Id] = new MatchTarget(baseId, (short)material);
                            
                            if (id == state.Id)
                                continue;

                            Blocks[(int)material].States.Add(new BlockState(state.Id));
                        }
                        registered++;
                    }
                }

                Logger.LogDebug($"Successfully registered {registered} blocks..");
            }
            else
            {
                throw new FileNotFoundException("Failed to find blocks.json for registering block data.");
            }
        }

        public static async Task RegisterItemsAsync()
        {
            var file = new FileInfo("Assets/items.json");

            if (file.Exists)
            {
                using var fs = file.OpenRead();
                using var read = new StreamReader(fs, new UTF8Encoding(false));

                var json = await read.ReadToEndAsync();

                var type = JObject.Parse(json);

                using var enumerator = type.GetEnumerator();
                int registered = 0;

                while (enumerator.MoveNext())
                {
                    var (name, token) = enumerator.Current;

                    var itemName = name.Split(":")[1];

                    var item = JsonConvert.DeserializeObject<BaseRegistryJson>(token.ToString());

                    if (!Enum.TryParse(itemName.Replace("_", ""), true, out Materials material))
                        continue;

                    Logger.LogDebug($"Registered item: {material} with id: {item.ProtocolId}");

                    Items.Add(material, new Item(material) { Id = item.ProtocolId, UnlocalizedName = name });
                    registered++;
                }

                Logger.LogDebug($"Successfully registered {registered} items..");
            }

        }

        public static async Task RegisterBiomesAsync()
        {
            var file = new FileInfo("Assets/biomes.json");

            if (!file.Exists)
                return;

            using var fs = file.OpenRead();
            using var read = new StreamReader(fs, new UTF8Encoding(false));

            var json = await read.ReadToEndAsync();

            var type = JObject.Parse(json);

            using var enumerator = type.GetEnumerator();
            int registered = 0;

            while (enumerator.MoveNext())
            {
                var (name, token) = enumerator.Current;

                var itemName = name.Split(":")[1];

                var biomeDes = JsonConvert.DeserializeObject<BaseRegistryJson>(token.ToString());

                if (!Enum.TryParse(itemName.Replace("_", ""), true, out Biomes biome))
                    continue;

                Logger.LogDebug($"Registered biome: {biome} with id: {biomeDes.ProtocolId}");

                Biomes.Add(biome, biomeDes.ProtocolId);
                registered++;
            }

            Logger.LogDebug($"Successfully registered {registered} biomes..");

            var codecBiomes = new FileInfo("Assets/biome_dimension_codec.json");

            if (!codecBiomes.Exists)
                return;

            using var cfs = codecBiomes.OpenRead();
            using var cread = new StreamReader(cfs, new UTF8Encoding(false));

            json = await cread.ReadToEndAsync();

            type = JObject.Parse(json);

            using var cenumerator = type.GetEnumerator();

            registered = 0;
            while (cenumerator.MoveNext())
            {
                var (name, token) = cenumerator.Current;

                foreach (var obj in token)
                {
                    var val = obj.ToString();
                    var codec = JsonConvert.DeserializeObject<BiomeCodec>(val, Globals.JsonSettings);

                    DefaultBiomes.TryAdd(codec.Name, codec);

                    Logger.LogDebug($"Added codec: {codec.Name}:{codec.Id}");
                    registered++;
                }
            }
            Logger.LogDebug($"Successfully registered {registered} codec biomes");
        }

        public static async Task RegisterDimensionsAsync()
        {
            var dimesnions = new FileInfo("Assets/default_dimensions.json");

            if (!dimesnions.Exists)
                return;

            using var cfs = dimesnions.OpenRead();
            using var cread = new StreamReader(cfs, new UTF8Encoding(false));

            var json = await cread.ReadToEndAsync();

            var type = JObject.Parse(json);

            using var cenumerator = type.GetEnumerator();

            var registered = 0;
            while (cenumerator.MoveNext())
            {
                var (name, token) = cenumerator.Current;

                foreach (var obj in token)
                {
                    var val = obj.ToString();
                    var codec = JsonConvert.DeserializeObject<DimensionCodec>(val, Globals.JsonSettings);

                    DefaultDimensions.TryAdd(codec.Id, codec);

                    Logger.LogDebug($"Added codec: {codec.Name}:{codec.Id}");
                    registered++;
                }
            }
            Logger.LogDebug($"Successfully registered {registered} codec biomes");
        }

        public static async Task RegisterTagsAsync()
        {
            var domains = new Dictionary<string, List<DomainTag>>();
            var registered = 0;
            foreach (var directory in Directory.GetDirectories("Assets/Tags"))
            {
                var repl = directory.Replace(@"\", @"/");

                var baseTagName = repl.Split("/")[2];

                foreach (var file in Directory.GetFiles(directory))
                {
                    var fi = new FileInfo(file);

                    using var fs = fi.OpenRead();
                    using var read = new StreamReader(fs, new UTF8Encoding(false));

                    var json = await read.ReadToEndAsync();

                    var type = JObject.Parse(json);

                    using var enumerator = type.GetEnumerator();

                    while (enumerator.MoveNext())
                    {
                        var (name, token) = enumerator.Current;

                        if (name.EqualsIgnoreCase("values"))
                        {
                            var tagName = fi.Name.Replace(".json", "");

                            var list = token.ToObject<List<string>>();

                            var ids = new List<int>();

                            foreach (var item in list)
                            {
                                if (item.StartsWith("#"))
                                {
                                    var start = item.TrimStart('#');

                                    if (domains.ContainsKey(start))
                                        domains[start].Add(new DomainTag
                                        {
                                            TagName = tagName,
                                            BaseTagName = baseTagName
                                        });
                                    else
                                        domains.Add(start, new List<DomainTag>
                                        {
                                            new DomainTag
                                            {
                                                TagName = tagName,
                                                BaseTagName = baseTagName
                                            }
                                        });

                                    continue;
                                }

                                object obj = null;
                                switch (baseTagName)
                                {
                                    case "blocks":
                                        obj = GetBlock(item);
                                        break;
                                    case "items":
                                        obj = GetItem(item);
                                        break;
                                    default:
                                        if (Enum.TryParse<EntityType>(item.Replace("minecraft:", "").Replace("_", ""), true, out var entityType))
                                            obj = (int)entityType;
                                        else if (Enum.TryParse<Fluids>(item.Replace("minecraft:", "").Replace("_", ""), true, out var fluid))
                                            obj = (int)fluid;
                                        break;
                                }

                                if (obj is Block block)
                                    ids.Add(block.Id);
                                else if (obj is Item returnItem)
                                    ids.Add(returnItem.Id);
                                else if (obj is int value)
                                    ids.Add(value);
                            }

                            if (Tags.ContainsKey(baseTagName))
                            {
                                Tags[baseTagName].Add(new Tag
                                {
                                    Name = tagName,
                                    Entries = ids,
                                    Count = ids.Count
                                });
                            }
                            else
                            {
                                Tags.Add(baseTagName, new List<Tag>
                                {
                                    new Tag
                                    {
                                        Name = tagName,
                                        Entries = ids,
                                        Count = ids.Count
                                    }
                                });
                            }
                            Logger.LogDebug($"Registered tag {baseTagName}:{tagName}");
                        }
                    }
                    registered++;
                }
            }

            if (domains.Count > 0)
            {
                foreach (var (t, domainTags) in domains)
                {
                    var item = t.Replace("minecraft:", "");

                    foreach (var domainTag in domainTags)
                    {
                        var index = Tags[domainTag.BaseTagName].FindIndex(x => x.Name.EqualsIgnoreCase(item));

                        var tag = Tags[domainTag.BaseTagName][index];

                        var tagIndex = Tags[domainTag.BaseTagName].FindIndex(x => x.Name.EqualsIgnoreCase(domainTag.TagName));

                        Tags[domainTag.BaseTagName][tagIndex].Count += tag.Count;

                        Tags[domainTag.BaseTagName][tagIndex].Entries.AddRange(tag.Entries);

                        Logger.LogDebug($"Registering domain: {item} to {domainTag.BaseTagName}:{domainTag.TagName}");
                        registered++;
                    }

                }
            }

            Logger.LogDebug($"Registered { registered} tags");
        }

        public static Block GetBlock(Materials material) => Blocks[(int)material];

        public static Block GetBlock(int baseId) => Blocks.SingleOrDefault(x => x.Id == baseId);

        public static Block GetBlock(string unlocalizedName) =>
            Blocks.SingleOrDefault(x => x.UnlocalizedName.EqualsIgnoreCase(unlocalizedName));

        public static Item GetItem(int id) => Items.Values.SingleOrDefault(x => x.Id == id);
        public static Item GetItem(Materials mat) => Items.GetValueOrDefault(mat);
        public static Item GetItem(string unlocalizedName) =>
            Items.Values.SingleOrDefault(x => x.UnlocalizedName.EqualsIgnoreCase(unlocalizedName));

        class BaseRegistryJson
        {
            [JsonProperty("protocol_id")]
            public int ProtocolId { get; set; }
        }

    }

    public class DomainTag
    {
        public string TagName { get; set; }

        public string BaseTagName { get; set; }
    }


    public struct MatchTarget
    {
        public short @base;
        public short numeric;

        public MatchTarget(short @base, short numeric)
        {
            this.@base = @base;
            this.numeric = numeric;
        }
    }
}
