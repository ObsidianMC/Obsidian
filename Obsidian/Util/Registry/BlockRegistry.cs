using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Obsidian.Blocks;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Registry.Enums;
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
        public static Dictionary<Materials, Block> BlockStates = new Dictionary<Materials, Block>();

        public static async Task RegisterAllAsync()
        {
            var file = new FileInfo("Assets/blocks.json");

            if (file.Exists)
            {
                var json = "";
                using var fs = file.OpenRead();
                using var read = new StreamReader(fs, new UTF8Encoding(false));

                json = await read.ReadToEndAsync();

                int registered = 0;

                var type = JObject.Parse(json);

                using (var enumerator = type.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        var (name, token) = enumerator.Current;

                        var blockName = name.Split(":")[1];

                        var states = JsonConvert.DeserializeObject<BlockJson>(token.ToString(), Program.JsonSettings);

                        if (!Enum.TryParse(blockName.Replace("_", ""), true, out Materials material))
                            continue;

                        if (states.States.Length <= 0)
                            continue;

                        int id = 0;
                        foreach (var state in states.States)
                            id = state.Default ? state.Id : states.States.First().Id;

                        await Program.RegistryLogger.LogDebugAsync($"Registered block: {material} with id: {id}");

                        switch (material)
                        {
                            case Materials.Air:
                                BlockStates.Add(material, new BlockAir());
                                break;
                            case Materials.GrassBlock:
                                BlockStates.Add(material, new BlockGrass(blockName, id));
                                break;
                            case Materials.Podzol:
                                BlockStates.Add(material, new BlockDirtSnow());
                                break;
                            case Materials.OakSapling:
                                BlockStates.Add(material, new BlockSapling(blockName, id));
                                break;
                            case Materials.SpruceSapling:
                                BlockStates.Add(material, new BlockSapling(blockName, id));
                                break;
                            case Materials.BirchSapling:
                                BlockStates.Add(material, new BlockSapling(blockName, id));
                                break;
                            case Materials.JungleSapling:
                                BlockStates.Add(material, new BlockSapling(blockName, id));
                                break;
                            case Materials.AcaciaSapling:
                                BlockStates.Add(material, new BlockSapling(blockName, id));
                                break;
                            case Materials.DarkOakSapling:
                                BlockStates.Add(material, new BlockSapling(blockName, id));
                                break;
                            case Materials.Water:
                                BlockStates.Add(material, new BlockFluid(blockName, id));
                                break;
                            case Materials.Lava:
                                BlockStates.Add(material, new BlockFluid(blockName, id));
                                break;
                            case Materials.OakLog:
                                BlockStates.Add(material, new BlockLog(blockName, id));
                                break;
                            case Materials.SpruceLog:
                                BlockStates.Add(material, new BlockLog(blockName, id));
                                break;
                            case Materials.BirchLog:
                                BlockStates.Add(material, new BlockLog(blockName, id));
                                break;
                            case Materials.JungleLog:
                                BlockStates.Add(material, new BlockLog(blockName, id));
                                break;
                            case Materials.AcaciaLog:
                                BlockStates.Add(material, new BlockLog(blockName, id));
                                break;
                            case Materials.DarkOakLog:
                                BlockStates.Add(material, new BlockLog(blockName, id));
                                break;
                            case Materials.StrippedOakLog:
                                BlockStates.Add(material, new BlockLog(blockName, id));
                                break;
                            case Materials.StrippedSpruceLog:
                                BlockStates.Add(material, new BlockLog(blockName, id));
                                break;
                            case Materials.StrippedBirchLog:
                                BlockStates.Add(material, new BlockLog(blockName, id));
                                break;
                            case Materials.StrippedJungleLog:
                                BlockStates.Add(material, new BlockLog(blockName, id));
                                break;
                            case Materials.StrippedAcaciaLog:
                                BlockStates.Add(material, new BlockLog(blockName, id));
                                break;
                            case Materials.StrippedDarkOakLog:
                                BlockStates.Add(material, new BlockLog(blockName, id));
                                break;
                            case Materials.OakWood:
                                BlockStates.Add(material, new BlockRotatable(blockName, id));
                                break;
                            case Materials.SpruceWood:
                                BlockStates.Add(material, new BlockRotatable(blockName, id));
                                break;
                            case Materials.BirchWood:
                                BlockStates.Add(material, new BlockRotatable(blockName, id));
                                break;
                            case Materials.JungleWood:
                                BlockStates.Add(material, new BlockRotatable(blockName, id));
                                break;
                            case Materials.AcaciaWood:
                                BlockStates.Add(material, new BlockRotatable(blockName, id));
                                break;
                            case Materials.DarkOakWood:
                                BlockStates.Add(material, new BlockRotatable(blockName, id));
                                break;
                            case Materials.StrippedOakWood:
                                BlockStates.Add(material, new BlockRotatable(blockName, id));
                                break;
                            case Materials.StrippedSpruceWood:
                                BlockStates.Add(material, new BlockRotatable(blockName, id));
                                break;
                            case Materials.StrippedBirchWood:
                                BlockStates.Add(material, new BlockRotatable(blockName, id));
                                break;
                            case Materials.StrippedJungleWood:
                                BlockStates.Add(material, new BlockRotatable(blockName, id));
                                break;
                            case Materials.StrippedAcaciaWood:
                                BlockStates.Add(material, new BlockRotatable(blockName, id));
                                break;
                            case Materials.StrippedDarkOakWood:
                                BlockStates.Add(material, new BlockRotatable(blockName, id));
                                break;
                            case Materials.OakLeaves:
                                BlockStates.Add(material, new BlockLeaves(blockName, id));
                                break;
                            case Materials.SpruceLeaves:
                                BlockStates.Add(material, new BlockLeaves(blockName, id));
                                break;
                            case Materials.BirchLeaves:
                                BlockStates.Add(material, new BlockLeaves(blockName, id));
                                break;
                            case Materials.JungleLeaves:
                                BlockStates.Add(material, new BlockLeaves(blockName, id));
                                break;
                            case Materials.AcaciaLeaves:
                                BlockStates.Add(material, new BlockLeaves(blockName, id));
                                break;
                            case Materials.DarkOakLeaves:
                                BlockStates.Add(material, new BlockLeaves(blockName, id));
                                break;
                            case Materials.Sponge:
                                BlockStates.Add(material, new BlockSponge(blockName, id));
                                break;
                            case Materials.WetSponge:
                                BlockStates.Add(material, new BlockSponge(blockName, id));
                                break;
                            case Materials.Dispenser:
                                BlockStates.Add(material, new BlockDispenser(blockName, id));
                                break;
                            case Materials.NoteBlock:
                                BlockStates.Add(material, new BlockNote(blockName, id));
                                break;
                            case Materials.WhiteBed:
                                BlockStates.Add(material, new BlockBed(blockName, id));
                                break;
                            case Materials.OrangeBed:
                                BlockStates.Add(material, new BlockBed(blockName, id));
                                break;
                            case Materials.MagentaBed:
                                BlockStates.Add(material, new BlockBed(blockName, id));
                                break;
                            case Materials.LightBlueBed:
                                BlockStates.Add(material, new BlockBed(blockName, id));
                                break;
                            case Materials.YellowBed:
                                BlockStates.Add(material, new BlockBed(blockName, id));
                                break;
                            case Materials.LimeBed:
                                BlockStates.Add(material, new BlockBed(blockName, id));
                                break;
                            case Materials.PinkBed:
                                BlockStates.Add(material, new BlockBed(blockName, id));
                                break;
                            case Materials.GrayBed:
                                BlockStates.Add(material, new BlockBed(blockName, id));
                                break;
                            case Materials.LightGrayBed:
                                BlockStates.Add(material, new BlockBed(blockName, id));
                                break;
                            case Materials.CyanBed:
                                BlockStates.Add(material, new BlockBed(blockName, id));
                                break;
                            case Materials.PurpleBed:
                                BlockStates.Add(material, new BlockBed(blockName, id));
                                break;
                            case Materials.BlueBed:
                                BlockStates.Add(material, new BlockBed(blockName, id));
                                break;
                            case Materials.BrownBed:
                                BlockStates.Add(material, new BlockBed(blockName, id));
                                break;
                            case Materials.GreenBed:
                                BlockStates.Add(material, new BlockBed(blockName, id));
                                break;
                            case Materials.RedBed:
                                BlockStates.Add(material, new BlockBed(blockName, id));
                                break;
                            case Materials.BlackBed:
                                BlockStates.Add(material, new BlockBed(blockName, id));
                                break;
                            case Materials.PoweredRail:
                                BlockStates.Add(material, new BlockPoweredRail(blockName, id));
                                break;
                            case Materials.DetectorRail:
                                BlockStates.Add(material, new BlockMinecartDetector(blockName, id));
                                break;
                            case Materials.StickyPiston:
                                BlockStates.Add(material, new BlockPiston(blockName, id));
                                break;
                            case Materials.Cobweb:
                                BlockStates.Add(material, new BlockWeb(blockName, id));
                                break;
                            case Materials.Grass:
                                BlockStates.Add(material, new BlockLongGrass(blockName, id));
                                break;
                            case Materials.Fern:
                                BlockStates.Add(material, new BlockLongGrass(blockName, id));
                                break;
                            case Materials.DeadBush:
                                BlockStates.Add(material, new BlockDeadBush(blockName, id));
                                break;
                            case Materials.Seagrass:
                                BlockStates.Add(material, new BlockSeaGrass(blockName, id));
                                break;
                            case Materials.TallSeagrass:
                                BlockStates.Add(material, new BlockTallSeaGrass(blockName, id));
                                break;
                            case Materials.Piston:
                                BlockStates.Add(material, new BlockPiston(blockName, id));
                                break;
                            case Materials.PistonHead:
                                BlockStates.Add(material, new BlockPistonExtension(blockName, id));
                                break;
                            case Materials.MovingPiston:
                                BlockStates.Add(material, new BlockPiston(blockName, id));
                                break;
                            case Materials.Dandelion:
                                BlockStates.Add(material, new BlockFlower(blockName, id));
                                break;
                            case Materials.Poppy:
                                BlockStates.Add(material, new BlockFlower(blockName, id));
                                break;
                            case Materials.BlueOrchid:
                                BlockStates.Add(material, new BlockFlower(blockName, id));
                                break;
                            case Materials.Allium:
                                BlockStates.Add(material, new BlockFlower(blockName, id));
                                break;
                            case Materials.AzureBluet:
                                BlockStates.Add(material, new BlockFlower(blockName, id));
                                break;
                            case Materials.RedTulip:
                                BlockStates.Add(material, new BlockFlower(blockName, id));
                                break;
                            case Materials.OrangeTulip:
                                BlockStates.Add(material, new BlockFlower(blockName, id));
                                break;
                            case Materials.WhiteTulip:
                                BlockStates.Add(material, new BlockFlower(blockName, id));
                                break;
                            case Materials.PinkTulip:
                                BlockStates.Add(material, new BlockFlower(blockName, id));
                                break;
                            case Materials.OxeyeDaisy:
                                BlockStates.Add(material, new BlockFlower(blockName, id));
                                break;
                            case Materials.BrownMushroom:
                                BlockStates.Add(material, new BlockMushroom(blockName, id));
                                break;
                            case Materials.RedMushroom:
                                BlockStates.Add(material, new BlockMushroom(blockName, id));
                                break;
                            case Materials.Tnt:
                                BlockStates.Add(material, new BlockTnt(blockName, id));
                                break;
                            case Materials.Torch:
                                BlockStates.Add(material, new BlockTorch(blockName, id));
                                break;
                            case Materials.WallTorch:
                                BlockStates.Add(material, new BlockWallTorch(blockName, id));
                                break;
                            case Materials.Fire:
                                BlockStates.Add(material, new BlockFire(blockName, id));
                                break;
                            case Materials.Spawner:
                                BlockStates.Add(material, new BlockMobSpawner(blockName, id));
                                break;
                            case Materials.OakStairs:
                                BlockStates.Add(material, new BlockStairs(blockName, id));
                                break;
                            case Materials.Chest:
                                BlockStates.Add(material, new BlockChest(blockName, id));
                                break;
                            case Materials.RedstoneWire:
                                BlockStates.Add(material, new BlockRedstoneWire(blockName, id));
                                break;
                            case Materials.CraftingTable:
                                BlockStates.Add(material, new BlockWorkbench(blockName, id));
                                break;
                            case Materials.Wheat:
                                BlockStates.Add(material, new BlockCrops(blockName, id));
                                break;
                            case Materials.Farmland:
                                BlockStates.Add(material, new BlockSoil(blockName, id));
                                break;
                            case Materials.Furnace:
                                BlockStates.Add(material, new BlockFurnace(blockName, id));
                                break;
                            case Materials.AcaciaSign://TODO signs
                                BlockStates.Add(material, new BlockFloorSign(blockName, id));
                                break;
                            case Materials.BirchSign:
                                BlockStates.Add(material, new BlockFloorSign(blockName, id));
                                break;
                            case Materials.DarkOakSign:
                                BlockStates.Add(material, new BlockFloorSign(blockName, id));
                                break;
                            case Materials.JungleSign:
                                BlockStates.Add(material, new BlockFloorSign(blockName, id));
                                break;
                            case Materials.OakSign:
                                BlockStates.Add(material, new BlockFloorSign(blockName, id));
                                break;
                            case Materials.SpruceSign:
                                BlockStates.Add(material, new BlockFloorSign(blockName, id));
                                break;
                            case Materials.OakDoor:
                                BlockStates.Add(material, new BlockDoor(blockName, id));
                                break;
                            case Materials.Ladder:
                                BlockStates.Add(material, new BlockLadder(blockName, id));
                                break;
                            case Materials.Rail:
                                BlockStates.Add(material, new BlockMinecartTrack(blockName, id));
                                break;
                            case Materials.CobblestoneStairs:
                                BlockStates.Add(material, new BlockStairs(blockName, id));
                                break;
                            case Materials.AcaciaWallSign://TODO different wall signs
                                BlockStates.Add(material, new BlockWallSign(blockName, id));
                                break;
                            case Materials.BirchWallSign:
                                BlockStates.Add(material, new BlockWallSign(blockName, id));
                                break;
                            case Materials.DarkOakWallSign:
                                BlockStates.Add(material, new BlockWallSign(blockName, id));
                                break;
                            case Materials.JungleWallSign:
                                BlockStates.Add(material, new BlockWallSign(blockName, id));
                                break;
                            case Materials.OakWallSign:
                                BlockStates.Add(material, new BlockWallSign(blockName, id));
                                break;
                            case Materials.SpruceWallSign:
                                BlockStates.Add(material, new BlockWallSign(blockName, id));
                                break;
                            case Materials.Lever:
                                BlockStates.Add(material, new BlockLever(blockName, id));
                                break;
                            case Materials.StonePressurePlate:
                                BlockStates.Add(material, new BlockPressurePlate(blockName, id));
                                break;
                            case Materials.IronDoor:
                                BlockStates.Add(material, new BlockDoor(blockName, id));
                                break;
                            case Materials.OakPressurePlate:
                                BlockStates.Add(material, new BlockPressurePlate(blockName, id));
                                break;
                            case Materials.SprucePressurePlate:
                                BlockStates.Add(material, new BlockPressurePlate(blockName, id));
                                break;
                            case Materials.BirchPressurePlate:
                                BlockStates.Add(material, new BlockPressurePlate(blockName, id));
                                break;
                            case Materials.JunglePressurePlate:
                                BlockStates.Add(material, new BlockPressurePlate(blockName, id));
                                break;
                            case Materials.AcaciaPressurePlate:
                                BlockStates.Add(material, new BlockPressurePlate(blockName, id));
                                break;
                            case Materials.DarkOakPressurePlate:
                                BlockStates.Add(material, new BlockPressurePlate(blockName, id));
                                break;
                            case Materials.RedstoneTorch:
                                BlockStates.Add(material, new BlockRedstoneTorch(blockName, id));
                                break;
                            case Materials.RedstoneWallTorch:
                                BlockStates.Add(material, new BlockRedstoneWallTorch(blockName, id));
                                break;
                            case Materials.StoneButton:
                                BlockStates.Add(material, new BlockStoneButton(blockName, id));
                                break;
                            case Materials.Snow:
                                BlockStates.Add(material, new BlockSnow(blockName, id));
                                break;
                            case Materials.Ice:
                                BlockStates.Add(material, new BlockIce(blockName, id));
                                break;
                            case Materials.Cactus:
                                BlockStates.Add(material, new BlockCactus(blockName, id));
                                break;
                            case Materials.Clay:
                                BlockStates.Add(material, new Block(blockName, id));
                                break;
                            case Materials.SugarCane:
                                BlockStates.Add(material, new BlockReed(blockName, id));
                                break;
                            case Materials.Jukebox:
                                BlockStates.Add(material, new BlockJukebox(blockName, id));
                                break;
                            case Materials.OakFence:
                                BlockStates.Add(material, new BlockFence(blockName, id));
                                break;
                            case Materials.Pumpkin:
                                BlockStates.Add(material, new BlockPumpkin(blockName, id));
                                break;
                            case Materials.SoulSand:
                                BlockStates.Add(material, new BlockSlowSand(blockName, id));
                                break;
                            case Materials.NetherPortal:
                                BlockStates.Add(material, new BlockPortal(blockName, id));
                                break;
                            case Materials.CarvedPumpkin:
                                BlockStates.Add(material, new BlockPumpkinCarved(blockName, id));
                                break;
                            case Materials.JackOLantern:
                                BlockStates.Add(material, new BlockPumpkinCarved(blockName, id));
                                break;
                            case Materials.Cake:
                                BlockStates.Add(material, new BlockCake(blockName, id));
                                break;
                            case Materials.Repeater:
                                BlockStates.Add(material, new BlockRepeater(blockName, id));
                                break;
                            case Materials.WhiteStainedGlass:
                                BlockStates.Add(material, new BlockStainedGlass(blockName, id));
                                break;
                            case Materials.OrangeStainedGlass:
                                BlockStates.Add(material, new BlockStainedGlass(blockName, id));
                                break;
                            case Materials.MagentaStainedGlass:
                                BlockStates.Add(material, new BlockStainedGlass(blockName, id));
                                break;
                            case Materials.LightBlueStainedGlass:
                                BlockStates.Add(material, new BlockStainedGlass(blockName, id));
                                break;
                            case Materials.YellowStainedGlass:
                                BlockStates.Add(material, new BlockStainedGlass(blockName, id));
                                break;
                            case Materials.LimeStainedGlass:
                                BlockStates.Add(material, new BlockStainedGlass(blockName, id));
                                break;
                            case Materials.PinkStainedGlass:
                                BlockStates.Add(material, new BlockStainedGlass(blockName, id));
                                break;
                            case Materials.GrayStainedGlass:
                                BlockStates.Add(material, new BlockStainedGlass(blockName, id));
                                break;
                            case Materials.LightGrayStainedGlass:
                                BlockStates.Add(material, new BlockStainedGlass(blockName, id));
                                break;
                            case Materials.CyanStainedGlass:
                                BlockStates.Add(material, new BlockStainedGlass(blockName, id));
                                break;
                            case Materials.PurpleStainedGlass:
                                BlockStates.Add(material, new BlockStainedGlass(blockName, id));
                                break;
                            case Materials.BlueStainedGlass:
                                BlockStates.Add(material, new BlockStainedGlass(blockName, id));
                                break;
                            case Materials.BrownStainedGlass:
                                BlockStates.Add(material, new BlockStainedGlass(blockName, id));
                                break;
                            case Materials.GreenStainedGlass:
                                BlockStates.Add(material, new BlockStainedGlass(blockName, id));
                                break;
                            case Materials.RedStainedGlass:
                                BlockStates.Add(material, new BlockStainedGlass(blockName, id));
                                break;
                            case Materials.BlackStainedGlass:
                                BlockStates.Add(material, new BlockStainedGlass(blockName, id));
                                break;
                            case Materials.OakTrapdoor:
                                BlockStates.Add(material, new BlockTrapdoor(blockName, id));
                                break;
                            case Materials.SpruceTrapdoor:
                                BlockStates.Add(material, new BlockTrapdoor(blockName, id));
                                break;
                            case Materials.BirchTrapdoor:
                                BlockStates.Add(material, new BlockTrapdoor(blockName, id));
                                break;
                            case Materials.JungleTrapdoor:
                                BlockStates.Add(material, new BlockTrapdoor(blockName, id));
                                break;
                            case Materials.AcaciaTrapdoor:
                                BlockStates.Add(material, new BlockTrapdoor(blockName, id));
                                break;
                            case Materials.DarkOakTrapdoor:
                                BlockStates.Add(material, new BlockTrapdoor(blockName, id));
                                break;
                            case Materials.InfestedStone:
                                BlockStates.Add(material, new BlockMonsterEggs(blockName, id));
                                break;
                            case Materials.InfestedCobblestone:
                                BlockStates.Add(material, new BlockMonsterEggs(blockName, id));
                                break;
                            case Materials.InfestedStoneBricks:
                                BlockStates.Add(material, new BlockMonsterEggs(blockName, id));
                                break;
                            case Materials.InfestedMossyStoneBricks:
                                BlockStates.Add(material, new BlockMonsterEggs(blockName, id));
                                break;
                            case Materials.InfestedCrackedStoneBricks:
                                BlockStates.Add(material, new BlockMonsterEggs(blockName, id));
                                break;
                            case Materials.InfestedChiseledStoneBricks:
                                BlockStates.Add(material, new BlockMonsterEggs(blockName, id));
                                break;
                            case Materials.BrownMushroomBlock:
                                BlockStates.Add(material, new BlockHugeMushroom(blockName, id));
                                break;
                            case Materials.RedMushroomBlock:
                                BlockStates.Add(material, new BlockHugeMushroom(blockName, id));
                                break;
                            case Materials.MushroomStem:
                                BlockStates.Add(material, new BlockHugeMushroom(blockName, id));
                                break;
                            case Materials.IronBars:
                                BlockStates.Add(material, new BlockIronBars(blockName, id));
                                break;
                            case Materials.GlassPane:
                                BlockStates.Add(material, new BlockIronBars(blockName, id));
                                break;
                            case Materials.Melon:
                                BlockStates.Add(material, new BlockMelon(blockName, id));
                                break;
                            case Materials.AttachedPumpkinStem:
                                BlockStates.Add(material, new BlockStemAttached(blockName, id));
                                break;
                            case Materials.AttachedMelonStem:
                                BlockStates.Add(material, new BlockStemAttached(blockName, id));
                                break;
                            case Materials.PumpkinStem:
                                BlockStates.Add(material, new BlockStem(blockName, id));
                                break;
                            case Materials.MelonStem:
                                BlockStates.Add(material, new BlockStem(blockName, id));
                                break;
                            case Materials.Vine:
                                BlockStates.Add(material, new BlockVine(blockName, id));
                                break;
                            case Materials.OakFenceGate:
                                BlockStates.Add(material, new BlockFenceGate(blockName, id));
                                break;
                            case Materials.BrickStairs:
                                BlockStates.Add(material, new BlockStairs(blockName, id));
                                break;
                            case Materials.StoneBrickStairs:
                                BlockStates.Add(material, new BlockStairs(blockName, id));
                                break;
                            case Materials.Mycelium:
                                BlockStates.Add(material, new BlockMycel(blockName, id));
                                break;
                            case Materials.LilyPad:
                                BlockStates.Add(material, new BlockWaterLily(blockName, id));
                                break;
                            case Materials.NetherBrickFence:
                                BlockStates.Add(material, new BlockFence(blockName, id));
                                break;
                            case Materials.NetherBrickStairs:
                                BlockStates.Add(material, new BlockStairs(blockName, id));
                                break;
                            case Materials.NetherWart:
                                BlockStates.Add(material, new BlockNetherWart(blockName, id));
                                break;
                            case Materials.EnchantingTable:
                                BlockStates.Add(material, new BlockEnchantingTable(blockName, id));
                                break;
                            case Materials.BrewingStand:
                                BlockStates.Add(material, new BlockBrewingStand(blockName, id));
                                break;
                            case Materials.Cauldron:
                                BlockStates.Add(material, new BlockCauldron(blockName, id));
                                break;
                            default:
                                BlockStates.Add(material, new Block(blockName, id));
                                break;
                        }
                        registered++;
                    }
                }

                await Program.RegistryLogger.LogDebugAsync($"Successfully registered {registered} blocks..");
            }
            else
            {
                throw new InvalidOperationException("Failed to find blocks.json for registering block data.");
            }
        }

        public static Block GetBlock(Materials mat)
        {
            if (BlockStates.TryGetValue(mat, out Block result))
                return result;

            return null;
        }

        public static Block FromId(int id)
        {
            foreach (var (key, value) in BlockStates)
            {
                if (value.Id == id)
                    return value;
            }

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

        [JsonProperty("honey_level")]
        public int[] HoneyLevels { get; set; }

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

        [JsonProperty("mode")]
        public string[] Mode { get; set; }

        [JsonProperty("attachment")]
        public string[] Attachments { get; set; }
    }

    public class BlockPropertiesJson
    {
        public int Stage { get; set; }
        public int Level { get; set; }
        public int Note { get; set; }
        public int Age { get; set; }
        public int Power { get; set; }
        public int Moisture { get; set; }
        public int Rotation { get; set; }
        public int Layers { get; set; }
        public int Bites { get; set; }
        public int Delay { get; set; }
        public int HoneyLevel { get; set; }
        public int Distance { get; set; }

        public string East { get; set; }
        public string North { get; set; }
        public string South { get; set; }
        public string Up { get; set; }
        public string West { get; set; }
        public string Bottom { get; set; }

        public bool Snowy { get; set; }
        public bool Powered { get; set; }
        public bool Triggered { get; set; }
        public bool Occupied { get; set; }
        public bool Unstable { get; set; }
        public bool Waterlogged { get; set; }
        public bool Lit { get; set; }
        public bool SignalFire { get; set; }
        public bool Hanging { get; set; }
        public bool Opened { get; set; }
        public bool HasRecord { get; set; }
        public bool Locked { get; set; }
        public bool HasBottle0 { get; set; }
        public bool HasBottle1 { get; set; }
        public bool HasBottle2 { get; set; }
        public bool HasBook { get; set; }
        public bool Disarmed { get; set; }
        public bool Attached { get; set; }

        public Axis Axis { get; set; }
        public BlockFace Facing { get; set; }
        public Instruments Instrument { get; set; }
        public Part Part { get; set; }
        public Shape Shape { get; set; }
        public Half Half { get; set; }
        public MinecraftType Type { get; set; }
        public Hinge Hinge { get; set; }
        public Face Face { get; set; }
        public Mode Mode { get; set; }
        public Attachment Attachment { get; set; }
    }
}
