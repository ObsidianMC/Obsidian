using Obsidian.Util;
using System.Collections.Generic;

namespace Obsidian.BlockData
{
    public class Blocks
    {
        public static List<Block> BLOCK_STATES { get; } = new List<Block>();

        public static Block Air => Add(new BlockAir());
        public static Block Stone => Add("stone", 1);
        public static Block Granite => Add("granite", 2);
        public static Block PolishedGranite => Add("polished_granite", 3);
        public static Block Diorite => Add("diorite", 4);
        public static Block PolishedDiorite => Add("polished_diorite", 5);
        public static Block Andesite => Add("andesite", 6);
        public static Block PolishedAndesite => Add("polished_andesite", 7);
        public static Block Grass => Add("grass", 8); // 9
        public static Block Dirt => Add("dirt", 10);
        public static Block CoarseDirt => Add("coarse_dirt", 11);
        public static Block Podzol => Add("podzol", 12); // 13
        public static Block Cobblestone => Add("cobblestone", 14);
        public static Block OakPlanks => Add("oak_planks", 15);
        public static Block SprucePlanks => Add("spruce_planks", 16);
        public static Block BirchPlanks => Add("birch_planks", 17);
        public static Block JunglePlanks => Add("jungle_planks", 18);
        public static Block AcaciaPlanks => Add("acacia_planks", 19);
        public static Block DarkOakPlanks => Add("dark_oak_planks", 20);
        public static Block OakSapling => Add("oak_sapling", 21); // 22
        public static Block SpruceSapling => Add("spruce_sapling", 23); // 24
        public static Block BirchSapling => Add("birch_sapling", 25); //26
        public static Block JungleSapling => Add("jungle_sapling", 27); //28
        public static Block AcaciaSapling => Add("acacia_sapling", 29); // 30
        public static Block DarkOakSapling => Add("dark_oak_sapling", 31); // 32
        public static Block Bedrock => Add("bedrock", 33);
        public static Block Water => Add("water", 34);
        public static Block Lava => Add("lava", 35);
        private static Block Add(Block block)
        {
            BLOCK_STATES.Add(block);
            return block;
        }

        private static Block Add(string name, int id)
        {
            var block = new Block(name, id);
            BLOCK_STATES.Add(block);
            return block;
        }
    }

    public class Block : BlockState
    {
        public byte Metadata { get; set; }

        public Position Position { get; set; }

        public Block(string name, int id) : base(name, id) { }

        public void Set(Block block)
        {
            this.Id = block.Id;
        }
    }

    public class BlockState
    {
        public int Id = 0;

        public string UnlocalizedName { get; }

        public BlockState(int id)
        {
            this.Id = id;
        }

        public BlockState(string unlocalizedName, int Id)
        {
            this.Id = Id;
            this.UnlocalizedName = unlocalizedName;
        }
    }
}
