using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Obsidian.BlockData;
using Obsidian.Logging;
using Obsidian.Util.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Util.Registry
{
    public class BlockRegistry
    {
        public static Dictionary<Materials, Block> BLOCK_STATES = new Dictionary<Materials, Block>();

        private static readonly AsyncLogger Logger = new AsyncLogger("Registry", LogLevel.Debug, "registry.log");

        public static async Task RegisterAll()
        {
            var file = new FileInfo("blocks.json");

            if (file.Exists)
            {
                var json = "";
                using (var fs = file.OpenRead())
                {
                    using (var read = new StreamReader(fs, new UTF8Encoding(false)))
                    {
                        json = await read.ReadToEndAsync();
                    }
                }

                int registered = 0;

                var type = JObject.Parse(json);

                using (var enumerator = type.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        var (name, token) = enumerator.Current;

                        var blockName = name.Split(":")[1];

                        var states = JsonConvert.DeserializeObject<BlockJson>(token.ToString(), new MinecraftCustomDirectionConverter(), new MinecraftAxisConverter(), new MinecraftFaceConverter(), 
                            new MinecraftFacesConverter(), new MinecraftHalfConverter(), new MinecraftHingeConverter(), new MinecraftInstrumentConverter(), new MinecraftPartConverter(), new MinecraftShapeConverter(), new MinecraftTypeConverter());

                        if (Enum.TryParse(blockName.Replace("_", ""), true, out Materials material))
                        {
                            int id = states.States.FirstOrDefault().Id;
                            Logger.LogDebug($"Registered block: {material.ToString()} with id: {id.ToString()}");
                            
                            switch (material)
                            {
                                case Materials.Air:
                                    BLOCK_STATES.Add(material, new BlockAir());
                                    break;
                                case Materials.GrassBlock:
                                    BLOCK_STATES.Add(material, new BlockGrass(blockName, id));
                                    break;
                                case Materials.Podzol:
                                    BLOCK_STATES.Add(material, new BlockDirtSnow());
                                    break;
                                case Materials.OakSapling:
                                    BLOCK_STATES.Add(material, new BlockSapling(blockName, id));
                                    break;
                                case Materials.SpruceSapling:
                                    BLOCK_STATES.Add(material, new BlockSapling(blockName, id));
                                    break;
                                case Materials.BirchSapling:
                                    BLOCK_STATES.Add(material, new BlockSapling(blockName, id));
                                    break;
                                case Materials.JungleSapling:
                                    BLOCK_STATES.Add(material, new BlockSapling(blockName, id));
                                    break;
                                case Materials.AcaciaSapling:
                                    BLOCK_STATES.Add(material, new BlockSapling(blockName, id));
                                    break;
                                case Materials.DarkOakSapling:
                                    BLOCK_STATES.Add(material, new BlockSapling(blockName, id));
                                    break;
                                case Materials.Water:
                                    BLOCK_STATES.Add(material, new BlockFluid(blockName, id));
                                    break;
                                case Materials.Lava:
                                    BLOCK_STATES.Add(material, new BlockFluid(blockName, id));
                                    break;
                                case Materials.OakLog:
                                    BLOCK_STATES.Add(material, new BlockLog(blockName, id));
                                    break;
                                case Materials.SpruceLog:
                                    BLOCK_STATES.Add(material, new BlockLog(blockName, id));
                                    break;
                                case Materials.BirchLog:
                                    BLOCK_STATES.Add(material, new BlockLog(blockName, id));
                                    break;
                                case Materials.JungleLog:
                                    BLOCK_STATES.Add(material, new BlockLog(blockName, id));
                                    break;
                                case Materials.AcaciaLog:
                                    BLOCK_STATES.Add(material, new BlockLog(blockName, id));
                                    break;
                                case Materials.DarkOakLog:
                                    BLOCK_STATES.Add(material, new BlockLog(blockName, id));
                                    break;
                                case Materials.StrippedOakLog:
                                    BLOCK_STATES.Add(material, new BlockLog(blockName, id));
                                    break;
                                case Materials.StrippedSpruceLog:
                                    BLOCK_STATES.Add(material, new BlockLog(blockName, id));
                                    break;
                                case Materials.StrippedBirchLog:
                                    BLOCK_STATES.Add(material, new BlockLog(blockName, id));
                                    break;
                                case Materials.StrippedJungleLog:
                                    BLOCK_STATES.Add(material, new BlockLog(blockName, id));
                                    break;
                                case Materials.StrippedAcaciaLog:
                                    BLOCK_STATES.Add(material, new BlockLog(blockName, id));
                                    break;
                                case Materials.StrippedDarkOakLog:
                                    BLOCK_STATES.Add(material, new BlockLog(blockName, id));
                                    break;
                                case Materials.OakWood:
                                    BLOCK_STATES.Add(material, new BlockRotatable(blockName, id));
                                    break;
                                case Materials.SpruceWood:
                                    BLOCK_STATES.Add(material, new BlockRotatable(blockName, id));
                                    break;
                                case Materials.BirchWood:
                                    BLOCK_STATES.Add(material, new BlockRotatable(blockName, id));
                                    break;
                                case Materials.JungleWood:
                                    BLOCK_STATES.Add(material, new BlockRotatable(blockName, id));
                                    break;
                                case Materials.AcaciaWood:
                                    BLOCK_STATES.Add(material, new BlockRotatable(blockName, id));
                                    break;
                                case Materials.DarkOakWood:
                                    BLOCK_STATES.Add(material, new BlockRotatable(blockName, id));
                                    break;
                                case Materials.StrippedOakWood:
                                    BLOCK_STATES.Add(material, new BlockRotatable(blockName, id));
                                    break;
                                case Materials.StrippedSprucehWood:
                                    BLOCK_STATES.Add(material, new BlockRotatable(blockName, id));
                                    break;
                                case Materials.StrippedBirchWood:
                                    BLOCK_STATES.Add(material, new BlockRotatable(blockName, id));
                                    break;
                                case Materials.StrippedJungleWood:
                                    BLOCK_STATES.Add(material, new BlockRotatable(blockName, id));
                                    break;
                                case Materials.StrippedAcaciaWood:
                                    BLOCK_STATES.Add(material, new BlockRotatable(blockName, id));
                                    break;
                                case Materials.StrippedDarkOakWood:
                                    BLOCK_STATES.Add(material, new BlockRotatable(blockName, id));
                                    break;
                                case Materials.OakLeaves:
                                    BLOCK_STATES.Add(material, new BlockLeaves(blockName, id));
                                    break;
                                case Materials.SpruceLeaves:
                                    BLOCK_STATES.Add(material, new BlockLeaves(blockName, id));
                                    break;
                                case Materials.BirchLeaves:
                                    BLOCK_STATES.Add(material, new BlockLeaves(blockName, id));
                                    break;
                                case Materials.JungleLeaves:
                                    BLOCK_STATES.Add(material, new BlockLeaves(blockName, id));
                                    break;
                                case Materials.AcaciaLeaves:
                                    BLOCK_STATES.Add(material, new BlockLeaves(blockName, id));
                                    break;
                                case Materials.DarkOakLeaves:
                                    BLOCK_STATES.Add(material, new BlockLeaves(blockName, id));
                                    break;
                                case Materials.Sponge:
                                    BLOCK_STATES.Add(material, new BlockSponge(blockName, id));
                                    break;
                                case Materials.WetSponge:
                                    BLOCK_STATES.Add(material, new BlockSponge(blockName, id));
                                    break;
                                case Materials.Dispenser:
                                    BLOCK_STATES.Add(material, new BlockDispenser(blockName, id));
                                    break;
                                case Materials.NoteBlock:
                                    BLOCK_STATES.Add(material, new BlockNote(blockName, id));
                                    break;
                                case Materials.WhiteBed:
                                    BLOCK_STATES.Add(material, new BlockBed(blockName, id));
                                    break;
                                case Materials.OrangeBed:
                                    BLOCK_STATES.Add(material, new BlockBed(blockName, id));
                                    break;
                                case Materials.MagentaBed:
                                    BLOCK_STATES.Add(material, new BlockBed(blockName, id));
                                    break;
                                case Materials.LightBlueBed:
                                    BLOCK_STATES.Add(material, new BlockBed(blockName, id));
                                    break;
                                case Materials.YellowBed:
                                    BLOCK_STATES.Add(material, new BlockBed(blockName, id));
                                    break;
                                case Materials.LimeBed:
                                    BLOCK_STATES.Add(material, new BlockBed(blockName, id));
                                    break;
                                case Materials.PinkBed:
                                    BLOCK_STATES.Add(material, new BlockBed(blockName, id));
                                    break;
                                case Materials.GrayBed:
                                    BLOCK_STATES.Add(material, new BlockBed(blockName, id));
                                    break;
                                case Materials.LightGrayBed:
                                    BLOCK_STATES.Add(material, new BlockBed(blockName, id));
                                    break;
                                case Materials.CyanBed:
                                    BLOCK_STATES.Add(material, new BlockBed(blockName, id));
                                    break;
                                case Materials.PurpleBed:
                                    BLOCK_STATES.Add(material, new BlockBed(blockName, id));
                                    break;
                                case Materials.BlueBed:
                                    BLOCK_STATES.Add(material, new BlockBed(blockName, id));
                                    break;
                                case Materials.BrownBed:
                                    BLOCK_STATES.Add(material, new BlockBed(blockName, id));
                                    break;
                                case Materials.GreenBed:
                                    BLOCK_STATES.Add(material, new BlockBed(blockName, id));
                                    break;
                                case Materials.RedBed:
                                    BLOCK_STATES.Add(material, new BlockBed(blockName, id));
                                    break;
                                case Materials.BlackBed:
                                    BLOCK_STATES.Add(material, new BlockBed(blockName, id));
                                    break;
                                case Materials.PoweredRail:
                                    BLOCK_STATES.Add(material, new BlockPoweredRail(blockName, id));
                                    break;
                                case Materials.DetectorRail:
                                    BLOCK_STATES.Add(material, new BlockMinecartDetector(blockName, id));
                                    break;
                                case Materials.StickyPiston:
                                    BLOCK_STATES.Add(material, new BlockPiston(blockName, id));
                                    break;
                                case Materials.Cobweb:
                                    BLOCK_STATES.Add(material, new BlockWeb(blockName, id));
                                    break;
                                case Materials.Grass:
                                    BLOCK_STATES.Add(material, new BlockLongGrass(blockName, id));
                                    break;
                                case Materials.Fern:
                                    BLOCK_STATES.Add(material, new BlockLongGrass(blockName, id));
                                    break;
                                case Materials.DeadBush:
                                    BLOCK_STATES.Add(material, new BlockDeadBush(blockName, id));
                                    break;
                                case Materials.SeaGrass:
                                    BLOCK_STATES.Add(material, new BlockSeaGrass(blockName, id));
                                    break;
                                case Materials.TallSeaGrass:
                                    BLOCK_STATES.Add(material, new BlockTallSeaGrass(blockName, id));
                                    break;
                                case Materials.Piston:
                                    BLOCK_STATES.Add(material, new BlockPiston(blockName, id));
                                    break;
                                case Materials.PistonHead:
                                    BLOCK_STATES.Add(material, new BlockPistonExtension(blockName, id));
                                    break;
                                case Materials.MovingPiston:
                                    BLOCK_STATES.Add(material, new BlockPiston(blockName, id));
                                    break;
                                case Materials.Dandelion:
                                    BLOCK_STATES.Add(material, new BlockFlower(blockName, id));
                                    break;
                                case Materials.Poppy:
                                    BLOCK_STATES.Add(material, new BlockFlower(blockName, id));
                                    break;
                                case Materials.BlueOrchid:
                                    BLOCK_STATES.Add(material, new BlockFlower(blockName, id));
                                    break;
                                case Materials.Allium:
                                    BLOCK_STATES.Add(material, new BlockFlower(blockName, id));
                                    break;
                                case Materials.AzureBluet:
                                    BLOCK_STATES.Add(material, new BlockFlower(blockName, id));
                                    break;
                                case Materials.RedTulip:
                                    BLOCK_STATES.Add(material, new BlockFlower(blockName, id));
                                    break;
                                case Materials.OrangeTulip:
                                    BLOCK_STATES.Add(material, new BlockFlower(blockName, id));
                                    break;
                                case Materials.WhiteTulip:
                                    BLOCK_STATES.Add(material, new BlockFlower(blockName, id));
                                    break;
                                case Materials.PinkTulip:
                                    BLOCK_STATES.Add(material, new BlockFlower(blockName, id));
                                    break;
                                case Materials.OxeyeDaisy:
                                    BLOCK_STATES.Add(material, new BlockFlower(blockName, id));
                                    break;
                                case Materials.BrownMushroom:
                                    BLOCK_STATES.Add(material, new BlockMushroom(blockName, id));
                                    break;
                                case Materials.RedMushroom:
                                    BLOCK_STATES.Add(material, new BlockMushroom(blockName, id));
                                    break;
                                case Materials.Tnt:
                                    BLOCK_STATES.Add(material, new BlockTnt(blockName, id));
                                    break;
                                case Materials.Torch:
                                    BLOCK_STATES.Add(material, new BlockTorch(blockName, id));
                                    break;
                                case Materials.WallTorch:
                                    BLOCK_STATES.Add(material, new BlockWallTorch(blockName, id));
                                    break;
                                case Materials.Fire:
                                    BLOCK_STATES.Add(material, new BlockFire(blockName, id));
                                    break;
                                case Materials.Spawner:
                                    BLOCK_STATES.Add(material, new BlockMobSpawner(blockName, id));
                                    break;
                                case Materials.OakStairs:
                                    BLOCK_STATES.Add(material, new BlockStairs(blockName, id));
                                    break;
                                case Materials.Chest:
                                    BLOCK_STATES.Add(material, new BlockChest(blockName, id));
                                    break;
                                case Materials.RedstoneWire:
                                    BLOCK_STATES.Add(material, new BlockRedstoneWire(blockName, id));
                                    break;
                                case Materials.CraftingTable:
                                    BLOCK_STATES.Add(material, new BlockWorkbench(blockName, id));
                                    break;
                                case Materials.Wheat:
                                    BLOCK_STATES.Add(material, new BlockCrops(blockName, id));
                                    break;
                                case Materials.Farmland:
                                    BLOCK_STATES.Add(material, new BlockSoil(blockName, id));
                                    break;
                                case Materials.Furnace:
                                    BLOCK_STATES.Add(material, new BlockFurnace(blockName, id));
                                    break;
                                case Materials.Sign:
                                    BLOCK_STATES.Add(material, new BlockFloorSign(blockName, id));
                                    break;
                                case Materials.OakDoor:
                                    BLOCK_STATES.Add(material, new BlockDoor(blockName, id));
                                    break;
                                case Materials.Ladder:
                                    BLOCK_STATES.Add(material, new BlockLadder(blockName, id));
                                    break;
                                case Materials.Rail:
                                    BLOCK_STATES.Add(material, new BlockMinecartTrack(blockName, id));
                                    break;
                                case Materials.CobblestoneStairs:
                                    BLOCK_STATES.Add(material, new BlockStairs(blockName, id));
                                    break;
                                case Materials.WallSign:
                                    BLOCK_STATES.Add(material, new BlockWallSign(blockName, id));
                                    break;
                                case Materials.Lever:
                                    BLOCK_STATES.Add(material, new BlockLever(blockName, id));
                                    break;
                                case Materials.StonePressurePlate:
                                    BLOCK_STATES.Add(material, new BlockPressurePlate(blockName, id));
                                    break;
                                case Materials.IronDoor:
                                    BLOCK_STATES.Add(material, new BlockDoor(blockName, id));
                                    break;
                                case Materials.OakPressurePlate:
                                    BLOCK_STATES.Add(material, new BlockPressurePlate(blockName, id));
                                    break;
                                case Materials.SprucePressurePlate:
                                    BLOCK_STATES.Add(material, new BlockPressurePlate(blockName, id));
                                    break;
                                case Materials.BirchPressurePlate:
                                    BLOCK_STATES.Add(material, new BlockPressurePlate(blockName, id));
                                    break;
                                case Materials.JunglePressurePlate:
                                    BLOCK_STATES.Add(material, new BlockPressurePlate(blockName, id));
                                    break;
                                case Materials.AcaciaPressurePlate:
                                    BLOCK_STATES.Add(material, new BlockPressurePlate(blockName, id));
                                    break;
                                case Materials.DarkOakPressurePlate:
                                    BLOCK_STATES.Add(material, new BlockPressurePlate(blockName, id));
                                    break;
                                case Materials.RedstoneTorch:
                                    BLOCK_STATES.Add(material, new BlockRedstoneTorch(blockName, id));
                                    break;
                                case Materials.RedstoneWallTorch:
                                    BLOCK_STATES.Add(material, new BlockRedstoneWallTorch(blockName, id));
                                    break;
                                case Materials.StoneButton:
                                    BLOCK_STATES.Add(material, new BlockStoneButton(blockName, id));
                                    break;
                                case Materials.Snow:
                                    BLOCK_STATES.Add(material, new BlockSnow(blockName, id));
                                    break;
                                case Materials.Ice:
                                    BLOCK_STATES.Add(material, new BlockIce(blockName, id));
                                    break;
                                case Materials.Cactus:
                                    BLOCK_STATES.Add(material, new BlockCactus(blockName, id));
                                    break;
                                case Materials.Clay:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.SugarCane:
                                    BLOCK_STATES.Add(material, new BlockReed(blockName, id));
                                    break;
                                case Materials.Jukebox:
                                    BLOCK_STATES.Add(material, new BlockJukebox(blockName, id));
                                    break;
                                case Materials.OakFence:
                                    BLOCK_STATES.Add(material, new BlockFence(blockName, id));
                                    break;
                                case Materials.Pumpkin:
                                    BLOCK_STATES.Add(material, new BlockPumpkin(blockName, id));
                                    break;
                                case Materials.SoulSand:
                                    BLOCK_STATES.Add(material, new BlockSlowSand(blockName, id));
                                    break;
                                case Materials.NetherPortal:
                                    BLOCK_STATES.Add(material, new BlockPortal(blockName, id));
                                    break;
                                case Materials.CarvedPumpkin:
                                    BLOCK_STATES.Add(material, new BlockPumpkinCarved(blockName, id));
                                    break;
                                case Materials.JackOLantern:
                                    BLOCK_STATES.Add(material, new BlockPumpkinCarved(blockName, id));
                                    break;
                                case Materials.Cake:
                                    BLOCK_STATES.Add(material, new BlockCake(blockName, id));
                                    break;
                                case Materials.Repeater:
                                    BLOCK_STATES.Add(material, new BlockRepeater(blockName, id));
                                    break;
                                case Materials.WhiteStainedGlass:
                                    BLOCK_STATES.Add(material, new BlockStainedGlass(blockName, id));
                                    break;
                                case Materials.OrangeStainedGlass:
                                    BLOCK_STATES.Add(material, new BlockStainedGlass(blockName, id));
                                    break;
                                case Materials.MagentaStainedGlass:
                                    BLOCK_STATES.Add(material, new BlockStainedGlass(blockName, id));
                                    break;
                                case Materials.LightBlueStainedGlass:
                                    BLOCK_STATES.Add(material, new BlockStainedGlass(blockName, id));
                                    break;
                                case Materials.YellowStainedGlass:
                                    BLOCK_STATES.Add(material, new BlockStainedGlass(blockName, id));
                                    break;
                                case Materials.LimeStainedGlass:
                                    BLOCK_STATES.Add(material, new BlockStainedGlass(blockName, id));
                                    break;
                                case Materials.PinkStainedGlass:
                                    BLOCK_STATES.Add(material, new BlockStainedGlass(blockName, id));
                                    break;
                                case Materials.GrayStainedGlass:
                                    BLOCK_STATES.Add(material, new BlockStainedGlass(blockName, id));
                                    break;
                                case Materials.LightGrayStainedGlass:
                                    BLOCK_STATES.Add(material, new BlockStainedGlass(blockName, id));
                                    break;
                                case Materials.CyanStainedGlass:
                                    BLOCK_STATES.Add(material, new BlockStainedGlass(blockName, id));
                                    break;
                                case Materials.PurpleStainedGlass:
                                    BLOCK_STATES.Add(material, new BlockStainedGlass(blockName, id));
                                    break;
                                case Materials.BlueStainedGlass:
                                    BLOCK_STATES.Add(material, new BlockStainedGlass(blockName, id));
                                    break;
                                case Materials.BrownStainedGlass:
                                    BLOCK_STATES.Add(material, new BlockStainedGlass(blockName, id));
                                    break;
                                case Materials.GreenStainedGlass:
                                    BLOCK_STATES.Add(material, new BlockStainedGlass(blockName, id));
                                    break;
                                case Materials.RedStainedGlass:
                                    BLOCK_STATES.Add(material, new BlockStainedGlass(blockName, id));
                                    break;
                                case Materials.BlackStainedGlass:
                                    BLOCK_STATES.Add(material, new BlockStainedGlass(blockName, id));
                                    break;
                                case Materials.OakTrapdoor:
                                    BLOCK_STATES.Add(material, new BlockTrapdoor(blockName, id));
                                    break;
                                case Materials.SpruceTrapdoor:
                                    BLOCK_STATES.Add(material, new BlockTrapdoor(blockName, id));
                                    break;
                                case Materials.BirchTrapdoor:
                                    BLOCK_STATES.Add(material, new BlockTrapdoor(blockName, id));
                                    break;
                                case Materials.JungleTrapdoor:
                                    BLOCK_STATES.Add(material, new BlockTrapdoor(blockName, id));
                                    break;
                                case Materials.AcaciaTrapdoor:
                                    BLOCK_STATES.Add(material, new BlockTrapdoor(blockName, id));
                                    break;
                                case Materials.DarkOakTrapdoor:
                                    BLOCK_STATES.Add(material, new BlockTrapdoor(blockName, id));
                                    break;
                                case Materials.InfestedStone:
                                    BLOCK_STATES.Add(material, new BlockMonsterEggs(blockName, id));
                                    break;
                                case Materials.InfestedCobblestone:
                                    BLOCK_STATES.Add(material, new BlockMonsterEggs(blockName, id));
                                    break;
                                case Materials.InfestedStoneBricks:
                                    BLOCK_STATES.Add(material, new BlockMonsterEggs(blockName, id));
                                    break;
                                case Materials.InfestedMossyStoneBricks:
                                    BLOCK_STATES.Add(material, new BlockMonsterEggs(blockName, id));
                                    break;
                                case Materials.InfestedCrackedStoneBricks:
                                    BLOCK_STATES.Add(material, new BlockMonsterEggs(blockName, id));
                                    break;
                                case Materials.InfestedChiseledStoneBricks:
                                    BLOCK_STATES.Add(material, new BlockMonsterEggs(blockName, id));
                                    break;
                                case Materials.BrownMushroomBlock:
                                    BLOCK_STATES.Add(material, new BlockHugeMushroom(blockName, id));
                                    break;
                                case Materials.RedMushroomBlock:
                                    BLOCK_STATES.Add(material, new BlockHugeMushroom(blockName, id));
                                    break;
                                case Materials.MushroomStem:
                                    BLOCK_STATES.Add(material, new BlockHugeMushroom(blockName, id));
                                    break;
                                case Materials.IronBars:
                                    BLOCK_STATES.Add(material, new BlockIronBars(blockName, id));
                                    break;
                                case Materials.GlassPane:
                                    BLOCK_STATES.Add(material, new BlockIronBars(blockName, id));
                                    break;
                                case Materials.Melon:
                                    BLOCK_STATES.Add(material, new BlockMelon(blockName, id));
                                    break;
                                case Materials.AttachedPumpkinStem:
                                    BLOCK_STATES.Add(material, new BlockStemAttached(blockName, id));
                                    break;
                                case Materials.AttachedMelonStem:
                                    BLOCK_STATES.Add(material, new BlockStemAttached(blockName, id));
                                    break;
                                case Materials.PumpkinStem:
                                    BLOCK_STATES.Add(material, new BlockStem(blockName, id));
                                    break;
                                case Materials.MelonStem:
                                    BLOCK_STATES.Add(material, new BlockStem(blockName, id));
                                    break;
                                case Materials.Vine:
                                    BLOCK_STATES.Add(material, new BlockVine(blockName, id));
                                    break;
                                case Materials.OakFenceGate:
                                    BLOCK_STATES.Add(material, new BlockFenceGate(blockName, id));
                                    break;
                                case Materials.BrickStairs:
                                    BLOCK_STATES.Add(material, new BlockStairs(blockName, id));
                                    break;
                                case Materials.StoneBrickStairs:
                                    BLOCK_STATES.Add(material, new BlockStairs(blockName, id));
                                    break;
                                case Materials.Mycelium:
                                    BLOCK_STATES.Add(material, new BlockMycel(blockName, id));
                                    break;
                                case Materials.LilyPad:
                                    BLOCK_STATES.Add(material, new BlockWaterLily(blockName, id));
                                    break;
                                case Materials.NetherBrickFence:
                                    BLOCK_STATES.Add(material, new BlockFence(blockName, id));
                                    break;
                                case Materials.NetherBrickStairs:
                                    BLOCK_STATES.Add(material, new BlockStairs(blockName, id));
                                    break;
                                case Materials.NetherWart:
                                    BLOCK_STATES.Add(material, new BlockNetherWart(blockName, id));
                                    break;
                                case Materials.EnchantingTable:
                                    BLOCK_STATES.Add(material, new BlockEnchantingTable(blockName, id));
                                    break;
                                case Materials.BrewingStand:
                                    BLOCK_STATES.Add(material, new BlockBrewingStand(blockName, id));
                                    break;
                                case Materials.Cauldron:
                                    BLOCK_STATES.Add(material, new BlockCauldron(blockName, id));
                                    break;
                                default:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                            }
                            registered++;
                        }
                    }
                }

                Logger.LogDebug($"Successfully registered {registered} blocks..");
            }
            else
            {
                throw new InvalidOperationException("Failed to find blocks.json for registering block data.");
            }
        }

        public static Block G(Materials mat)
        {
            if (BLOCK_STATES.TryGetValue(mat, out Block result))
                return result;

            return null;
        }
    }

    public class BlockJson
    {
        [JsonProperty("states")]
        public BlockStateJson[] States { get; set; }

        [JsonProperty("properties")]
        public BlockPropertiesExtraJson Properties { get; set; }
    }

    public class BlockStateJson
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("default")]
        public bool Default { get; set; }

        [JsonProperty("properties")]
        public BlockPropertiesJson Properties { get; set; }
    }

    public class BlockPropertiesExtraJson
    {
        [JsonProperty("level")]
        public int[] Levels { get; set; }

        [JsonProperty("note")]
        public int[] Notes { get; set; }

        [JsonProperty("age")]
        public int[] Ages { get; set; }

        [JsonProperty("power")]
        public int[] PowerStates { get; set; }

        [JsonProperty("moisture")]
        public int[] MoistureStates { get; set; }

        [JsonProperty("rotation")]
        public int[] RotationStates { get; set; }

        [JsonProperty("layers")]
        public int[] Layers { get; set; }

        [JsonProperty("bites")]
        public int[] BiteStates { get; set; }

        [JsonProperty("delay")]
        public int[] DelayStates { get; set; }

        [JsonProperty("snowy")]
        public bool[] SnowyStates { get; set; }

        [JsonProperty("triggered")]
        public bool[] TriggeredStates { get; set; }

        [JsonProperty("powered")]
        public bool[] PoweredStates { get; set; }

        [JsonProperty("occupied")]
        public bool[] OccupiedStates { get; set; }

        [JsonProperty("extended")]
        public bool[] ExtendedStates { get; set; }

        [JsonProperty("unstable")]
        public bool[] UnstableStates { get; set; }

        [JsonProperty("waterlogged")]
        public bool[] WaterloggedStates { get; set; }

        [JsonProperty("lit")]
        public bool[] LitStates { get; set; }

        [JsonProperty("open")]
        public bool[] OpenedStates { get; set; }

        [JsonProperty("has_record")]
        public bool[] HasRecordStates { get; set; }

        [JsonProperty("locked")]
        public bool[] Locked { get; set; }

        [JsonProperty("has_bottle_0")]
        public bool[] HasBottle0States { get; set; }

        [JsonProperty("has_bottle_1")]
        public bool[] HasBottle1States { get; set; }

        [JsonProperty("has_bottle_2")]
        public bool[] HasBottle2States { get; set; }

        [JsonProperty("up")]
        public string[] UpStates { get; set; }

        [JsonProperty("down")]
        public string[] DownStates { get; set; }

        [JsonProperty("east")]
        public string[] EastStates { get; set; }

        [JsonProperty("north")]
        public string[] NorthStates { get; set; }

        [JsonProperty("South")]
        public string[] SouthStates { get; set; }

        [JsonProperty("west")]
        public string[] WestStates { get; set; }

        [JsonProperty("axis")]
        public string[] Axis { get; set; }

        [JsonProperty("facing")]
        public string[] Faces { get; set; }

        [JsonProperty("instrument")]
        public string[] Instruments { get; set; }

        [JsonProperty("part")]
        public string[] Parts { get; set; }

        [JsonProperty("shape")]
        public string[] Shapes { get; set; }

        [JsonProperty("half")]
        public string[] HalfStates { get; set; }

        [JsonProperty("type")]
        public string[] Types { get; set; }

        [JsonProperty("hinge")]
        public string[] Hinges { get; set; }
    }

    public class BlockPropertiesJson
    {
        [JsonProperty("stage")]
        public int Stage { get; set; }

        [JsonProperty("level")]
        public int Level { get; set; }

        [JsonProperty("note")]
        public int Note { get; set; }

        [JsonProperty("age")]
        public int Age { get; set; }

        [JsonProperty("power")]
        public int Power { get; set; }

        [JsonProperty("moisture")]
        public int Moisture { get; set; }

        [JsonProperty("rotation")]
        public int Rotation { get; set; }

        [JsonProperty("layers")]
        public int Layers { get; set; }

        [JsonProperty("bites")]
        public int Bites { get; set; }

        [JsonProperty("delay")]
        public int Delay { get; set; }

        [JsonProperty("snowy")]
        public bool Snowy { get; set; }

        [JsonProperty("powered")]
        public bool Powered { get; set; }

        [JsonProperty("triggered")]
        public bool Triggered { get; set; }

        [JsonProperty("occupied")]
        public bool Occupied { get; set; }

        [JsonProperty("unstable")]
        public bool Unstable { get; set; }

        [JsonProperty("waterlogged")]
        public bool Waterlogged { get; set; }

        [JsonProperty("lit")]
        public bool Lit { get; set; }

        [JsonProperty("open")]
        public bool Opened { get; set; }

        [JsonProperty("has_record")]
        public bool HasRecord { get; set; }

        [JsonProperty("locked")]
        public bool Locked { get; set; }

        [JsonProperty("has_bottle_0")]
        public bool HasBottle0 { get; set; }

        [JsonProperty("has_bottle_1")]
        public bool HasBottle1 { get; set; }

        [JsonProperty("has_bottle_2")]
        public bool HasBottle2 { get; set; }

        [JsonProperty("east")]
        public CustomDirection East { get; set; }

        [JsonProperty("north")]
        public CustomDirection North { get; set; }

        [JsonProperty("South")]
        public CustomDirection South { get; set; }

        [JsonProperty("up")]
        public CustomDirection Up { get; set; }

        [JsonProperty("down")]
        public CustomDirection Down { get; set; }

        [JsonProperty("west")]
        public CustomDirection West { get; set; }

        [JsonProperty("axis")]
        public Axis Axis { get; set; }

        [JsonProperty("facing")]
        public BlockFace Facing { get; set; }

        [JsonProperty("instrument")]
        public Instruments Instrument { get; set; }

        [JsonProperty("part")]
        public Part Part { get; set; }

        [JsonProperty("shape")]
        public Shape Shape { get; set; }

        [JsonProperty("half")]
        public Half Half { get; set; }

        [JsonProperty("type")]
        public MinecraftType Type { get; set; }

        [JsonProperty("hinge")]
        public Hinge Hinge { get; set; }

        [JsonProperty("face")]
        public SignFace Face { get; set; }
    }

    public enum Hinge
    {
        Left,

        Right
    }

    public enum CustomDirection
    {
        //Fire, fences, MushroomBlocks
        True,
        False,

        //Redstone
        Up,
        Side,
        None
    }

    public enum MinecraftType
    {
        //Pistons
        Normal,
        Sticky,

        //Chests
        Single,
        Left,
        Right
    }

    public enum Half
    {
        //for beds and doors
        Upper,
        Lower,
        
        //for stairs and trap doors
        Top,
        Bottom
    }

    public enum Shape
    {
        // For rails
        NorthSouth,
        EastWest,
        AscendingEast,
        AscendingWest,
        AscendingNorth,
        AscendingSouth,
        SouthEast,
        SouthWest,
        NorthWest,
        NorthEast,

        //for stairs
        Straight,
        InnerLeft,
        InnerRight,
        OuterLeft,
        OuterRight
    }

    public enum Part
    {
        Head,

        Foot
    }

    public enum Instruments
    {
        Harp,

        Basedrum,

        Snare,

        Hat,

        Bass,

        Flute,

        Bell,

        Guitar,

        Chime,

        Xylophone
    }

    public enum BlockFace
    {
        North,
        East,
        South,
        West,
        Up,
        Down   
    }

    public enum SignFace
    {
        Floor,
        Wall,
        Ceiling
    }

    public enum Axis
    {
        X,

        Y,

        Z
    }
}
