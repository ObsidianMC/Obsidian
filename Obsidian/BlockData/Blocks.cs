namespace Obsidian.BlockData
{
    /*internal class Blocks
    {
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

        public static Block Redstone;

        public static Block DiamondOre;
        public static Block DiamondBlock;

        public static Block CraftingTable;

        public static Block Wheat;

        public static Block Farmland;

        public static Block Furnace;

        public static Block OakSign;
        public static Block SpruceSign;
        public static Block BirchSign;
        public static Block AcaciaSign;
        public static Block JungleSign;
        public static Block DarkOakSign;

        public static Block OakDoor;

        public static Block Ladder;
        public static Block Rail;

        public static Block CobblestoneStairs;

        public static Block OakWallSign;
        public static Block SpruceWallSign;
        public static Block BirchWallSign;
        public static Block JungleWallSign;
        public static Block AcaciaWallSign;
        public static Block DarkOakWallSign;

        public static async Task LoadBlocksAsync()
        {
            Air = await BlockRegistry.RegisterAsync(new BlockAir());

            Stone = await BlockRegistry.RegisterAsync(new Block("stone", 1));
            Granite = await BlockRegistry.RegisterAsync(new Block("granite", 2));
            PolishedGranite = await BlockRegistry.RegisterAsync(new Block("polished_granite", 3));
            Diorite = await BlockRegistry.RegisterAsync(new Block("diorite", 4));
            PolishedDiorite = await BlockRegistry.RegisterAsync(new Block("polished_diorite", 5));
            Andesite = await BlockRegistry.RegisterAsync(new Block("andesite", 6));
            PolishedAndesite = await BlockRegistry.RegisterAsync(new Block("polished_andesite", 7));

            Grass = await BlockRegistry.RegisterAsync(new BlockGrass()); // +1
            Dirt = await BlockRegistry.RegisterAsync(new Block("dirt", 10));
            CoarseDirt = await BlockRegistry.RegisterAsync(new Block("coarse_dirt", 11)); // +1
            Podzol = await BlockRegistry.RegisterAsync(new BlockDirtSnow()); // +1

            Cobblestone = await BlockRegistry.RegisterAsync(new Block("cobblestone", 14));

            OakPlanks = await BlockRegistry.RegisterAsync(new Block("oak_planks", 15));
            SprucePlanks = await BlockRegistry.RegisterAsync(new Block("spruce_planks", 16));
            BirchPlanks = await BlockRegistry.RegisterAsync(new Block("birch_planks", 17));
            JunglePlanks = await BlockRegistry.RegisterAsync(new Block("jungle_planks", 18));
            AcaciaPlanks = await BlockRegistry.RegisterAsync(new Block("acacia_planks", 19));
            DarkOakPlanks = await BlockRegistry.RegisterAsync(new Block("dark_oak_planks", 20));

            OakSapling = await BlockRegistry.RegisterAsync(new BlockSapling("oak_sapling", 21)); // +1
            SpruceSapling = await BlockRegistry.RegisterAsync(new BlockSapling("spruce_sapling", 23)); // +1
            BirchSapling = await BlockRegistry.RegisterAsync(new BlockSapling("birch_sapling", 25)); // +1
            JungleSapling = await BlockRegistry.RegisterAsync(new BlockSapling("jungle_sapling", 27)); // +1
            AcaciaSapling = await BlockRegistry.RegisterAsync(new BlockSapling("acacia_sapling", 29)); // +1
            DarkOakSapling = await BlockRegistry.RegisterAsync(new BlockSapling("dark_oak_sapling", 31)); // +1

            Bedrock = await BlockRegistry.RegisterAsync("bedrock", 33);
            Water = await BlockRegistry.RegisterAsync(new BlockFluid("water", 34)); // +15
            Lava = await BlockRegistry.RegisterAsync(new BlockFluid("lava", 50)); // +15

            Sand = await BlockRegistry.RegisterAsync(new BlockSand("sand", 66));
            RedSand = await BlockRegistry.RegisterAsync(new BlockSand("red_sand", 67));
            Gravel = await BlockRegistry.RegisterAsync(new Block("gravel", 68));

            GoldOre = await BlockRegistry.RegisterAsync(new BlockOre("gold_ore", 69));
            IronOre = await BlockRegistry.RegisterAsync(new BlockOre("iron_ore", 70));
            CoalOre = await BlockRegistry.RegisterAsync(new BlockOre("coal_ore", 71));

            OakLog = await BlockRegistry.RegisterAsync(new BlockLog("oak_log", 72)); // +2
            SpruceLog = await BlockRegistry.RegisterAsync(new BlockLog("spruce_log", 75)); // +2
            BirchLog = await BlockRegistry.RegisterAsync(new BlockLog("birch_log", 78)); // +2
            JungleLog = await BlockRegistry.RegisterAsync(new BlockLog("jungle_log", 81)); // +2
            AcaciaLog = await BlockRegistry.RegisterAsync(new BlockLog("acacia_log", 84));// +2
            DarkOakLog = await BlockRegistry.RegisterAsync(new BlockLog("dark_oak_log", 87)); // +2

            StrippedSpruceLog = await BlockRegistry.RegisterAsync(new BlockLog("stripped_oak_log", 90)); // +2
            StrippedBirchLog = await BlockRegistry.RegisterAsync(new BlockLog("stripped_spruce_log", 93)); // +2
            StrippedJungleLog = await BlockRegistry.RegisterAsync(new BlockLog("stripped_birch_log", 96)); // +2
            StrippedAcaciaLog = await BlockRegistry.RegisterAsync(new BlockLog("stripped_jungle_log", 99)); // +2
            StrippedDarkOakLog = await BlockRegistry.RegisterAsync(new BlockLog("stripped_acacia_log", 102)); // +2
            StrippedOakLog = await BlockRegistry.RegisterAsync(new BlockLog("stripped_dark_oak_log", 105)); // +2

            OakLeaves = await BlockRegistry.RegisterAsync(new BlockLeaves("oak_leaves", 148)); //+10
            SpruceLeaves = await BlockRegistry.RegisterAsync(new BlockLeaves("spruce_leaves", 158)); // +14
            BirchLeaves = await BlockRegistry.RegisterAsync(new BlockLeaves("birch_leaves", 172)); // +14
            JungleLeaves = await BlockRegistry.RegisterAsync(new BlockLeaves("jungle_leaves", 186)); // +14
            AcaciaLeaves = await BlockRegistry.RegisterAsync(new BlockLeaves("acacia_leaves", 200)); // +14
            DarkOakLeaves = await BlockRegistry.RegisterAsync(new BlockLeaves("dark_oak_leaves", 214)); // +14

            Sponge = await BlockRegistry.RegisterAsync(new BlockSponge("sponge", 228));
            WetSponge = await BlockRegistry.RegisterAsync(new BlockSponge("wet_sponge", 229));

            Glass = await BlockRegistry.RegisterAsync(new BlockGlass("glass", 230));

            LapisOre = await BlockRegistry.RegisterAsync(new BlockOre("lapis_ore", 231));
            LapisBlock = await BlockRegistry.RegisterAsync(new Block("lapis_block", 232));

            Dispenser = await BlockRegistry.RegisterAsync(new BlockDispenser("dispenser", 233)); // +12

            Sandstone = await BlockRegistry.RegisterAsync(new Block("sandstone", 245));
            ChiseledSandstone = await BlockRegistry.RegisterAsync(new Block("chiseled_sandstone", 246));
            CutSandstone = await BlockRegistry.RegisterAsync(new Block("cut_sandstone", 247));

            NoteBlock = await BlockRegistry.RegisterAsync(new BlockNote("note_block", 248)); //+ 499

            WhiteBed = await BlockRegistry.RegisterAsync(new BlockBed("white_bed", 748)); // +256
            OrangeBed = await BlockRegistry.RegisterAsync(new BlockBed("orange_bed", 748));
            MagentaBed = await BlockRegistry.RegisterAsync(new BlockBed("magenta_bed", 748));
            LightBlueBed = await BlockRegistry.RegisterAsync(new BlockBed("light_blue_bed", 748));
            YellowBed = await BlockRegistry.RegisterAsync(new BlockBed("yellow_bed", 748));
            LimeBed = await BlockRegistry.RegisterAsync(new BlockBed("lime_bed", 748));
            PinkBed = await BlockRegistry.RegisterAsync(new BlockBed("pink_bed", 748));
            GrayBed = await BlockRegistry.RegisterAsync(new BlockBed("gray_bed", 748));
            LightGrayBed = await BlockRegistry.RegisterAsync(new BlockBed("light_gray_bed", 748));
            CyanBed = await BlockRegistry.RegisterAsync(new BlockBed("cyan_bed", 748));
            PurpleBed = await BlockRegistry.RegisterAsync(new BlockBed("purple_bed", 748));
            BlueBed = await BlockRegistry.RegisterAsync(new BlockBed("blue_bed", 748));
            BrownBed = await BlockRegistry.RegisterAsync(new BlockBed("brown_bed", 748));
            GreenBed = await BlockRegistry.RegisterAsync(new BlockBed("green_bed", 748));
            RedBed = await BlockRegistry.RegisterAsync(new BlockBed("red_bed", 748));
            BlackBed = await BlockRegistry.RegisterAsync(new BlockBed("black_bed", 748));

            PoweredRail = await BlockRegistry.RegisterAsync(new BlockPoweredRail("powered_rail", 1004)); // + 11
            DetectorRail = await BlockRegistry.RegisterAsync(new BlockMinecartDetector("powered_rail", 1016)); // + 11

            StickyPiston = await BlockRegistry.RegisterAsync(new BlockPiston("sticky_piston", 1028)); //+ 11

            Cobweb = await BlockRegistry.RegisterAsync(new BlockWeb("cobweb", 1040));

            Grass = await BlockRegistry.RegisterAsync(new BlockLongGrass("grass", 1041));
            Fern = await BlockRegistry.RegisterAsync(new BlockLongGrass("fern", 1042));
            DeadBush = await BlockRegistry.RegisterAsync(new BlockDeadBush("dead_bush", 1043));
            Seagrass = await BlockRegistry.RegisterAsync(new BlockSeaGrass("seagrass", 1044));
            TallSeagrass = await BlockRegistry.RegisterAsync(new BlockTallSeaGrass("tall_seagrass", 1045)); //+ 2

            Piston = await BlockRegistry.RegisterAsync(new BlockPiston("piston", 1048)); //+ 10
            PistonHead = await BlockRegistry.RegisterAsync(new BlockPistonExtension("piston_head", 1059)); // + 23

            WhiteWool = await BlockRegistry.RegisterAsync(new Block("white_wool", 1083));
            OrangeWool = await BlockRegistry.RegisterAsync(new Block("orange_wool", 1084));
            MagentaWool = await BlockRegistry.RegisterAsync(new Block("magenta_wool", 1085));
            LightBlueWool = await BlockRegistry.RegisterAsync(new Block("light_blue_wool", 1086));
            YellowWool = await BlockRegistry.RegisterAsync(new Block("yellow_wool", 1087));
            LimeWool = await BlockRegistry.RegisterAsync(new Block("lime_wool", 1088));
            PinkWool = await BlockRegistry.RegisterAsync(new Block("pink_wool", 1089));
            GrayWool = await BlockRegistry.RegisterAsync(new Block("gray_wool", 1090));
            LightGrayWool = await BlockRegistry.RegisterAsync(new Block("light_gray_wool", 1091));
            CyanWool = await BlockRegistry.RegisterAsync(new Block("cyan_wool", 1092));
            PurpleWool = await BlockRegistry.RegisterAsync(new Block("purple_wool", 1093));
            BlueWool = await BlockRegistry.RegisterAsync(new Block("blue_wool", 1094));
            BrownWool = await BlockRegistry.RegisterAsync(new Block("brown_wool", 1095));
            GreenWool = await BlockRegistry.RegisterAsync(new Block("green_wool", 1096));
            RedWool = await BlockRegistry.RegisterAsync(new Block("red_wool", 1097));
            BlackWool = await BlockRegistry.RegisterAsync(new Block("black_wool", 1098));

            MovingPiston = await BlockRegistry.RegisterAsync(new Block("moving_piston", 1099)); // +12

            Dandelion = await BlockRegistry.RegisterAsync(new BlockFlower("dandelion", 1111));
            Poppy = await BlockRegistry.RegisterAsync(new BlockFlower("poppy", 1112));
            BlueOrchid = await BlockRegistry.RegisterAsync(new BlockFlower("blue_orchid", 1113));
            Allium = await BlockRegistry.RegisterAsync(new BlockFlower("allium", 1114));
            AzureBluet = await BlockRegistry.RegisterAsync(new BlockFlower("azure_bluet", 1115));
            RedTulip = await BlockRegistry.RegisterAsync(new BlockFlower("red_tulip", 1116));
            OrangeTulip = await BlockRegistry.RegisterAsync(new BlockFlower("orange_tulip", 1117));
            WhiteTulip = await BlockRegistry.RegisterAsync(new BlockFlower("white_tulip", 1118));
            PinkTulip = await BlockRegistry.RegisterAsync(new BlockFlower("dandelion", 1119));
            OxeyeDaisy = await BlockRegistry.RegisterAsync(new BlockFlower("oxeye_daisy", 1120));
            BrownMushroom = await BlockRegistry.RegisterAsync(new BlockMushroom("brown_mushroom", 1121));
            RedMushroom = await BlockRegistry.RegisterAsync(new BlockMushroom("brown_mushroom", 1122));

            GoldBlock = await BlockRegistry.RegisterAsync(new Block("gold_block", 1123));
            IronBlock = await BlockRegistry.RegisterAsync(new Block("iron_block", 1124));

            Bricks = await BlockRegistry.RegisterAsync(new Block("bricks", 1125));
            Tnt = await BlockRegistry.RegisterAsync(new Block("tnt", 1126)); // +1
            Bookshelf = await BlockRegistry.RegisterAsync(new Block("bookshelf", 1128));
            MossyCobblestone = await BlockRegistry.RegisterAsync(new Block("mossy_cobblestone", 1129));
            Obsidian = await BlockRegistry.RegisterAsync(new Block("obsidian", 1130));

            Torch = await BlockRegistry.RegisterAsync(new BlockTorch("torch", 1131)); // + 2
            WallTorch = await BlockRegistry.RegisterAsync(new BlockWallTorch("wall_torch", 1133)); // + 3

            Fire = await BlockRegistry.RegisterAsync(new BlockFire("fire", 1136)); // + 514
            Spawner = await BlockRegistry.RegisterAsync(new BlockMobSpawner("spawner", 1648));

            OakStairs = await BlockRegistry.RegisterAsync(new BlockStairs("oak_stairs", 1649)); // +79

            Chest = await BlockRegistry.RegisterAsync(new BlockChest("chest", 1729)); // + 23

            Redstone = await BlockRegistry.RegisterAsync(new BlockRedstoneWire("redstone", 1753)); // +1295

            DiamondOre = await BlockRegistry.RegisterAsync(new BlockOre("diamond_ore", 3049));
            DiamondBlock = await BlockRegistry.RegisterAsync(new Block("diamond_block", 3050));

            CraftingTable = await BlockRegistry.RegisterAsync(new BlockWorkbench("crafting_table", 3051));

            Wheat = await BlockRegistry.RegisterAsync(new BlockCrops("wheat", 3052)); //+7

            Farmland = await BlockRegistry.RegisterAsync(new BlockSoil("farmland", 3060)); // +7

            Furnace = await BlockRegistry.RegisterAsync(new BlockFurnace("furnace", 3068)); // + 7

            OakSign = await BlockRegistry.RegisterAsync(new BlockFloorSign("oak_sign", 3076)); // +33
            SpruceSign = await BlockRegistry.RegisterAsync(new BlockFloorSign("spruce_sign", 3076)); 
            BirchSign = await BlockRegistry.RegisterAsync(new BlockFloorSign("birch_sign", 3076)); 
            JungleSign = await BlockRegistry.RegisterAsync(new BlockFloorSign("jungle_sign", 3076)); 
            AcaciaSign = await BlockRegistry.RegisterAsync(new BlockFloorSign("acacia_sign", 3076)); 
            DarkOakSign = await BlockRegistry.RegisterAsync(new BlockFloorSign("dark_oak_sign", 3076));

            OakDoor = await BlockRegistry.RegisterAsync(new BlockDoor("oak_door", 3109));// +63

            Ladder = await BlockRegistry.RegisterAsync(new BlockLadder("ladder", 3172)); //+8
            Rail = await BlockRegistry.RegisterAsync(new BlockMinecartTrack("rail", 3180)); //+9

            CobblestoneStairs = await BlockRegistry.RegisterAsync(new BlockMinecartTrack("cobblestone_stairs", 3190)); // + 79

            OakWallSign = await BlockRegistry.RegisterAsync(new BlockMinecartTrack("oak_wall_sign", 3270)); // + 7
            SpruceWallSign = await BlockRegistry.RegisterAsync(new BlockMinecartTrack("spruce_wall_sign", 3270));
            BirchWallSign = await BlockRegistry.RegisterAsync(new BlockMinecartTrack("birch_wall_sign", 3270));
            JungleWallSign = await BlockRegistry.RegisterAsync(new BlockMinecartTrack("jungle_wall_sign", 3270));
            AcaciaWallSign = await BlockRegistry.RegisterAsync(new BlockMinecartTrack("acacia_wall_sign", 3270));
            DarkOakWallSign = await BlockRegistry.RegisterAsync(new BlockMinecartTrack("dark_oak_wall_sign", 3270));

            for (int i = 3278; i != 3303; i++) 
            {
                await BlockRegistry.RegisterAsync(new Block($"{i}_block", i));
            }
        }

        
    }*/
}