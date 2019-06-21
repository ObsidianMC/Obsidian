using Obsidian.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.BlockData
{
    //Plugin and such should not have access to this class
    public class Blocks
    {
        public static List<Block> BLOCK_STATES = new List<Block>();

        private static Logger Logger = new Logger("Registry", LogLevel.Debug);

        public static Block Air;

        public static Block Stone;
        public static Block Granite;
        public static Block PolishedGranite;
        public static Block Diorite;
        public static Block PolishedDiorite;
        public static Block Andesite;
        public static Block PolishedAndesite;

        public static Block GrassBlock;
        public static Block Dirt;
        public static Block CoarseDirt;
        public static Block Podzol;

        public static Block Cobblestone;

        public static Block OakPlanks;
        public static Block SprucePlanks;
        public static Block BirchPlanks;
        public static Block JunglePlanks;
        public static Block AcaciaPlanks;
        public static Block DarkOakPlanks;

        public static Block OakSapling;
        public static Block SpruceSapling;
        public static Block BirchSapling;
        public static Block JungleSapling;
        public static Block AcaciaSapling;
        public static Block DarkOakSapling;

        public static Block Bedrock;

        public static Block Water;
        public static Block Lava;

        public static Block Sand;
        public static Block RedSand;
        public static Block Gravel;

        public static Block GoldOre;
        public static Block IronOre;
        public static Block CoalOre;

        public static Block OakLog;
        public static Block SpruceLog;
        public static Block BirchLog;
        public static Block JungleLog;
        public static Block AcaciaLog;
        public static Block DarkOakLog;

        public static Block StrippedOakLog;
        public static Block StrippedSpruceLog;
        public static Block StrippedBirchLog;
        public static Block StrippedJungleLog;
        public static Block StrippedAcaciaLog;
        public static Block StrippedDarkOakLog;

        public static Block OakLeaves;
        public static Block SpruceLeaves;
        public static Block BirchLeaves;
        public static Block JungleLeaves;
        public static Block AcaciaLeaves;
        public static Block DarkOakLeaves;

        public static Block Sponge;
        public static Block WetSponge;

        public static Block Glass;

        public static Block LapisOre;
        public static Block LapisBlock;

        public static Block Dispenser;

        public static Block Sandstone;
        public static Block ChiseledSandstone;
        public static Block CutSandstone;

        public static Block NoteBlock;

        public static Block WhiteBed;
        public static Block OrangeBed;
        public static Block MagentaBed;
        public static Block LightBlueBed;
        public static Block YellowBed;
        public static Block LimeBed;
        public static Block PinkBed;
        public static Block GrayBed;
        public static Block LightGrayBed;
        public static Block CyanBed;
        public static Block PurpleBed;
        public static Block BlueBed;
        public static Block BrownBed;
        public static Block GreenBed;
        public static Block RedBed;
        public static Block BlackBed;

        public static Block PoweredRail;
        public static Block DetectorRail;

        public static Block StickyPiston;

        public static Block Cobweb;

        public static Block Grass;
        public static Block Fern;
        public static Block DeadBush;
        public static Block Seagrass;
        public static Block TallSeagrass;

        public static Block Piston;
        public static Block PistonHead;

        public static Block WhiteWool;
        public static Block OrangeWool;
        public static Block MagentaWool;
        public static Block LightBlueWool;
        public static Block YellowWool;
        public static Block LimeWool;
        public static Block PinkWool;
        public static Block GrayWool;
        public static Block LightGrayWool;
        public static Block CyanWool;
        public static Block PurpleWool;
        public static Block BlueWool;
        public static Block BrownWool;
        public static Block GreenWool;
        public static Block RedWool;
        public static Block BlackWool;

        public static Block MovingPiston;

        public static Block Dandelion;
        public static Block Poppy;
        public static Block BlueOrchid;
        public static Block Allium;
        public static Block AzureBluet;
        public static Block RedTulip;
        public static Block OrangeTulip;
        public static Block WhiteTulip;
        public static Block PinkTulip;
        public static Block OxeyeDaisy;

        //public static Block Cornflower; 1.14??
        //public static Block WitherRose;
        //public static Block LilyOfTheValley;
        public static Block BrownMushroom;

        public static Block RedMushroom;

        public static Block GoldBlock;
        public static Block IronBlock;

        public static Block Bricks;
        public static Block Tnt;
        public static Block Bookshelf;
        public static Block MossyCobblestone;
        public static Block Obsidian;

        public static Block Torch;
        public static Block WallTorch;

        public static Block Fire;
        public static Block Spawner;

        public static Block OakStairs;

        public static Block Chest;

        public static async Task RegisterAsync()
        {
            Air = await AddAsync(new BlockAir());

            Stone = await AddAsync(new Block("stone", 1));
            Granite = await AddAsync(new Block("granite", 2));
            PolishedGranite = await AddAsync(new Block("polished_granite", 3));
            Diorite = await AddAsync(new Block("diorite", 4));
            PolishedDiorite = await AddAsync(new Block("polished_diorite", 5));
            Andesite = await AddAsync(new Block("andesite", 6));
            PolishedAndesite = await AddAsync(new Block("polished_andesite", 7));

            Grass = await AddAsync(new BlockGrass()); // +1
            Dirt = await AddAsync(new Block("dirt", 10));
            CoarseDirt = await AddAsync(new Block("coarse_dirt", 11)); // +1
            Podzol = await AddAsync(new BlockDirtSnow()); // +1

            Cobblestone = await AddAsync(new Block("cobblestone", 14));

            OakPlanks = await AddAsync(new Block("oak_planks", 15));
            SprucePlanks = await AddAsync(new Block("spruce_planks", 16));
            BirchPlanks = await AddAsync(new Block("birch_planks", 17));
            JunglePlanks = await AddAsync(new Block("jungle_planks", 18));
            AcaciaPlanks = await AddAsync(new Block("acacia_planks", 19));
            DarkOakPlanks = await AddAsync(new Block("dark_oak_planks", 20));

            OakSapling = await AddAsync(new BlockSapling("oak_sapling", 21)); // +1
            SpruceSapling = await AddAsync(new BlockSapling("spruce_sapling", 23)); // +1
            BirchSapling = await AddAsync(new BlockSapling("birch_sapling", 25)); // +1
            JungleSapling = await AddAsync(new BlockSapling("jungle_sapling", 27)); // +1
            AcaciaSapling = await AddAsync(new BlockSapling("acacia_sapling", 29)); // +1
            DarkOakSapling = await AddAsync(new BlockSapling("dark_oak_sapling", 31)); // +1

            Bedrock = await AddAsync("bedrock", 33);
            Water = await AddAsync(new BlockFluid("water", 34)); // +15
            Lava = await AddAsync(new BlockFluid("lava", 50)); // +15

            Sand = await AddAsync(new BlockSand("sand", 66));
            RedSand = await AddAsync(new BlockSand("red_sand", 67));
            Gravel = await AddAsync(new Block("gravel", 68));

            GoldOre = await AddAsync(new BlockOre("gold_ore", 69));
            IronOre = await AddAsync(new BlockOre("iron_ore", 70));
            CoalOre = await AddAsync(new BlockOre("coal_ore", 71));

            OakLog = await AddAsync(new BlockLog("oak_log", 72)); // +2
            SpruceLog = await AddAsync(new BlockLog("spruce_log", 75)); // +2
            BirchLog = await AddAsync(new BlockLog("birch_log", 78)); // +2
            JungleLog = await AddAsync(new BlockLog("jungle_log", 81)); // +2
            AcaciaLog = await AddAsync(new BlockLog("acacia_log", 84));// +2
            DarkOakLog = await AddAsync(new BlockLog("dark_oak_log", 87)); // +2

            StrippedSpruceLog = await AddAsync(new BlockLog("stripped_oak_log", 90)); // +2
            StrippedBirchLog = await AddAsync(new BlockLog("stripped_spruce_log", 93)); // +2
            StrippedJungleLog = await AddAsync(new BlockLog("stripped_birch_log", 96)); // +2
            StrippedAcaciaLog = await AddAsync(new BlockLog("stripped_jungle_log", 99)); // +2
            StrippedDarkOakLog = await AddAsync(new BlockLog("stripped_acacia_log", 102)); // +2
            StrippedOakLog = await AddAsync(new BlockLog("stripped_dark_oak_log", 105)); // +2

            OakLeaves = await AddAsync(new BlockLeaves("oak_leaves", 148)); //+10
            SpruceLeaves = await AddAsync(new BlockLeaves("spruce_leaves", 158)); // +14
            BirchLeaves = await AddAsync(new BlockLeaves("birch_leaves", 172)); // +14
            JungleLeaves = await AddAsync(new BlockLeaves("jungle_leaves", 186)); // +14
            AcaciaLeaves = await AddAsync(new BlockLeaves("acacia_leaves", 200)); // +14
            DarkOakLeaves = await AddAsync(new BlockLeaves("dark_oak_leaves", 214)); // +14

            Sponge = await AddAsync(new BlockSponge("sponge", 228));
            WetSponge = await AddAsync(new BlockSponge("wet_sponge", 229));

            Glass = await AddAsync(new BlockGlass("glass", 230));

            LapisOre = await AddAsync(new BlockOre("lapis_ore", 231));
            LapisBlock = await AddAsync(new Block("lapis_block", 232));

            Dispenser = await AddAsync(new BlockDispenser("dispenser", 233)); // +12

            Sandstone = await AddAsync(new Block("sandstone", 245));
            ChiseledSandstone = await AddAsync(new Block("chiseled_sandstone", 246));
            CutSandstone = await AddAsync(new Block("cut_sandstone", 247));

            NoteBlock = await AddAsync(new BlockNote("note_block", 248)); //+ 499

            WhiteBed = await AddAsync(new BlockBed("white_bed", 748)); // +256
            OrangeBed = await AddAsync(new BlockBed("orange_bed", 748));
            MagentaBed = await AddAsync(new BlockBed("magenta_bed", 748));
            LightBlueBed = await AddAsync(new BlockBed("light_blue_bed", 748));
            YellowBed = await AddAsync(new BlockBed("yellow_bed", 748));
            LimeBed = await AddAsync(new BlockBed("lime_bed", 748));
            PinkBed = await AddAsync(new BlockBed("pink_bed", 748));
            GrayBed = await AddAsync(new BlockBed("gray_bed", 748));
            LightGrayBed = await AddAsync(new BlockBed("light_gray_bed", 748));
            CyanBed = await AddAsync(new BlockBed("cyan_bed", 748));
            PurpleBed = await AddAsync(new BlockBed("purple_bed", 748));
            BlueBed = await AddAsync(new BlockBed("blue_bed", 748));
            BrownBed = await AddAsync(new BlockBed("brown_bed", 748));
            GreenBed = await AddAsync(new BlockBed("green_bed", 748));
            RedBed = await AddAsync(new BlockBed("red_bed", 748));
            BlackBed = await AddAsync(new BlockBed("black_bed", 748));

            PoweredRail = await AddAsync(new BlockPoweredRail("powered_rail", 1004)); // + 11
            DetectorRail = await AddAsync(new BlockMinecartDetector("powered_rail", 1016)); // + 11

            StickyPiston = await AddAsync(new BlockPiston("sticky_piston", 1028)); //+ 11

            Cobweb = await AddAsync(new BlockWeb("cobweb", 1040));

            Grass = await AddAsync(new BlockLongGrass("grass", 1041));
            Fern = await AddAsync(new BlockLongGrass("fern", 1042));
            DeadBush = await AddAsync(new BlockDeadBush("dead_bush", 1043));
            Seagrass = await AddAsync(new BlockSeaGrass("seagrass", 1044));
            TallSeagrass = await AddAsync(new BlockTallSeaGrass("tall_seagrass", 1045)); //+ 2

            Piston = await AddAsync(new BlockPiston("piston", 1048)); //+ 10
            PistonHead = await AddAsync(new BlockPistonExtension("piston_head", 1059)); // + 23

            WhiteWool = await AddAsync(new Block("white_wool", 1083));
            OrangeWool = await AddAsync(new Block("orange_wool", 1084));
            MagentaWool = await AddAsync(new Block("magenta_wool", 1085));
            LightBlueWool = await AddAsync(new Block("light_blue_wool", 1086));
            YellowWool = await AddAsync(new Block("yellow_wool", 1087));
            LimeWool = await AddAsync(new Block("lime_wool", 1088));
            PinkWool = await AddAsync(new Block("pink_wool", 1089));
            GrayWool = await AddAsync(new Block("gray_wool", 1090));
            LightGrayWool = await AddAsync(new Block("light_gray_wool", 1091));
            CyanWool = await AddAsync(new Block("cyan_wool", 1092));
            PurpleWool = await AddAsync(new Block("purple_wool", 1093));
            BlueWool = await AddAsync(new Block("blue_wool", 1094));
            BrownWool = await AddAsync(new Block("brown_wool", 1095));
            GreenWool = await AddAsync(new Block("green_wool", 1096));
            RedWool = await AddAsync(new Block("red_wool", 1097));
            BlackWool = await AddAsync(new Block("black_wool", 1098));

            MovingPiston = await AddAsync(new Block("moving_piston", 1099)); // +12

            Dandelion = await AddAsync(new BlockFlower("dandelion", 1111));
            Poppy = await AddAsync(new BlockFlower("poppy", 1112));
            BlueOrchid = await AddAsync(new BlockFlower("blue_orchid", 1113));
            Allium = await AddAsync(new BlockFlower("allium", 1114));
            AzureBluet = await AddAsync(new BlockFlower("azure_bluet", 1115));
            RedTulip = await AddAsync(new BlockFlower("red_tulip", 1116));
            OrangeTulip = await AddAsync(new BlockFlower("orange_tulip", 1117));
            WhiteTulip = await AddAsync(new BlockFlower("white_tulip", 1118));
            PinkTulip = await AddAsync(new BlockFlower("dandelion", 1119));
            OxeyeDaisy = await AddAsync(new BlockFlower("oxeye_daisy", 1120));
            BrownMushroom = await AddAsync(new BlockMushroom("brown_mushroom", 1121));
            RedMushroom = await AddAsync(new BlockMushroom("brown_mushroom", 1122));

            GoldBlock = await AddAsync(new Block("gold_block", 1123));
            IronBlock = await AddAsync(new Block("iron_block", 1124));

            Bricks = await AddAsync(new Block("bricks", 1125));
            Tnt = await AddAsync(new Block("tnt", 1126)); // +1
            Bookshelf = await AddAsync(new Block("bookshelf", 1128));
            MossyCobblestone = await AddAsync(new Block("mossy_cobblestone", 1129));
            Obsidian = await AddAsync(new Block("obsidian", 1130));

            Torch = await AddAsync(new BlockTorch("torch", 1131)); // + 2
            WallTorch = await AddAsync(new BlockWallTorch("wall_torch", 1133)); // + 3

            Fire = await AddAsync(new BlockFire("fire", 1136)); // + 514
            Spawner = await AddAsync(new BlockMobSpawner("spawner", 1648));

            OakStairs = await AddAsync(new BlockStairs("oak_stairs", 1649)); // +79

            Chest = await AddAsync(new BlockChest("chest", 1729)); // + 23
            //for (int i = 1753; i < 1876; i++)
            //{
            //    await AddAsync(new Block($"{i}_block", i));
            //}
        }

        private static async Task<Block> AddAsync(Block block)
        {
            BLOCK_STATES.Add(block);
            await Logger.LogDebugAsync($"Registered: {block.UnlocalizedName} with id {block.Id}");
            return block;
        }

        private static async Task<Block> AddAsync(string name, int id)
        {
            var block = new Block(name, id);
            BLOCK_STATES.Add(block);
            await Logger.LogDebugAsync($"Registered: {name} with id {id}");
            return block;
        }
    }
}