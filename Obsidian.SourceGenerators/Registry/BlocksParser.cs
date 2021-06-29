using Microsoft.CodeAnalysis;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry
{
    internal sealed partial class BlocksParser : AssetsParser
    {
        public override string SourceFile => "blocks.json";
        public override string Name => "Blocks";

        public override void ParseAsset(GeneratorExecutionContext context, string asset)
        {
            BlockHandler blockHandler = context.Compilation.AssemblyName switch
            {
                "Obsidian" => new RegistryBuilder(),
                "Obsidian.API" => new BlockBuilder(),
                _ => null
            };

            if (blockHandler is null)
                return;
            blockHandler.Conjunctions = GetConjunctions();

            using var document = JsonDocument.Parse(asset);

            foreach (JsonProperty block in document.RootElement.EnumerateObject())
            {
                blockHandler.HandleBlock(context, block);
            }

            blockHandler.Complete(context);
        }

        private Conjunction[] GetConjunctions()
        {
            var blockColor = new Property("Color", "block_color", "BlockColor", new[] { "White", "Orange", "Magenta", "LightBlue", "Yellow", "Lime", "Pink", "Gray", "LightGray", "Cyan", "Purple", "Blue", "Brown", "Green", "Red", "Black" });
            var woodType = new Property("WoodType", "wood_type", "WoodType", new[] { "Oak", "Spruce", "Birch", "Jungle", "Acacia", "DarkOak" });
            var anvilDamage = new Property("Damage", "anvil_damage", "AnvilDamage", new[] { "None", "Chipped", "Damaged" });
            var infested = new Property("IsInfested", "infested", Property.BoolType, new[] { "true", "false" }, customOffset: 6, isBooleanFlipped: false);
            var stoneInfested = new Property(infested.Name, infested.Tag, infested.Type, infested.Values, customOffset: 4498, isBooleanFlipped: false);
            var cobblestoneInfested = new Property(infested.Name, infested.Tag, infested.Type, infested.Values, customOffset: 4486, isBooleanFlipped: false);
            return new Conjunction[]
            {
                    new Conjunction("Bed", blockColor, ColorizeTag("bed")),
                    new Conjunction("Banner", blockColor, ColorizeTag("banner")),
                    new Conjunction("WallBanner", blockColor, ColorizeTag("wall_banner")),
                    new Conjunction("Concrete", blockColor, ColorizeTag("concrete")),
                    new Conjunction("DyedShulkerBox", blockColor, ColorizeTag("shulker_box")),
                    new Conjunction("ConcretePowder", blockColor, ColorizeTag("concrete_powder")),
                    new Conjunction("StainedGlass", blockColor, ColorizeTag("stained_glass")),
                    new Conjunction("StainedGlassPane", blockColor, ColorizeTag("stained_glass_pane")),
                    new Conjunction("DyedTerracotta", blockColor, ColorizeTag("terracotta")),
                    new Conjunction("GlazedTerracotta", blockColor, ColorizeTag("glazed_terracotta")),
                    new Conjunction("Wool", blockColor, ColorizeTag("wool")),
                    new Conjunction("Carpet", blockColor, ColorizeTag("carpet")),
                    new Conjunction("Anvil", anvilDamage, "anvil", "chipped_anvil", "damaged_anvil"),
                    new Conjunction("Leaves", woodType, WoodifyTag("leaves")),
                    new Conjunction("Sapling", woodType, WoodifyTag("sapling")),
                    new Conjunction("WoodenButton", woodType, WoodifyTag("button")),
                    new Conjunction("WoodenPlanks", woodType, WoodifyTag("planks")),
                    new Conjunction("WoodenDoor", woodType, WoodifyTag("door")),
                    new Conjunction("WoodenFence", woodType, WoodifyTag("fence")),
                    new Conjunction("WoodenFenceGate", woodType, WoodifyTag("fence_gate")),
                    new Conjunction("WoodenPressurePlate", woodType, WoodifyTag("pressure_plate")),
                    new Conjunction("WoodenSign", woodType, WoodifyTag("sign")),
                    new Conjunction("WoodenWallSign", woodType, WoodifyTag("wall_sign")),
                    new Conjunction("WoodenSlab", woodType, WoodifyTag("slab")),
                    new Conjunction("WoodenStairs", woodType, WoodifyTag("stairs")),
                    new Conjunction("WoodenTrapdoor", woodType, WoodifyTag("trapdoor")),
                    new Conjunction("Wood", woodType, WoodifyTag("wood")),
                    new Conjunction("WoodLog", woodType, WoodifyTag("log")),
                    new Conjunction("StrippedWood", woodType, StripWoodifyTag("wood")),
                    new Conjunction("StrippedWoodLog", woodType, StripWoodifyTag("log")),
                    new Conjunction("Stone", stoneInfested, InfestifyTag("stone")),
                    new Conjunction("StoneBricks", infested, InfestifyTag("stone_bricks")),
                    new Conjunction("MossyStoneBricks", infested, InfestifyTag("mossy_stone_bricks")),
                    new Conjunction("CrackedStoneBricks", infested, InfestifyTag("cracked_stone_bricks")),
                    new Conjunction("ChiseledStoneBricks", infested, InfestifyTag("chiseled_stone_bricks")),
                    new Conjunction("Cobblestone", cobblestoneInfested, InfestifyTag("cobblestone")),
            };
        }

        private string[] ColorizeTag(string tag)
        {
            return new[] { $"white_{tag}", $"orange_{tag}", $"magenta_{tag}", $"light_blue_{tag}", $"yellow_{tag}", $"lime_{tag}", $"pink_{tag}", $"gray_{tag}", $"light_gray_{tag}", $"cyan_{tag}", $"purple_{tag}", $"blue_{tag}", $"brown_{tag}", $"green_{tag}", $"red_{tag}", $"black_{tag}" };
        }

        private string[] WoodifyTag(string tag)
        {
            return new[] { $"oak_{tag}", $"spruce_{tag}", $"birch_{tag}", $"jungle_{tag}", $"acacia_{tag}", $"dark_oak_{tag}" };
        }

        private string[] StripWoodifyTag(string tag)
        {
            return new[] { $"stripped_oak_{tag}", $"stripped_spruce_{tag}", $"stripped_birch_{tag}", $"stripped_jungle_{tag}", $"stripped_acacia_{tag}", $"stripped_dark_oak_{tag}" };
        }

        private string[] InfestifyTag(string tag)
        {
            return new[] { tag, $"infested_{tag}" };
        }

        private abstract class BlockHandler
        {
            public Conjunction[] Conjunctions { get; set; }
            public abstract void HandleBlock(GeneratorExecutionContext context, JsonProperty block);
            public abstract void Complete(GeneratorExecutionContext context);
        }

        // Combination of multiple blocks as one, e.g. multiple colors of wool
        private sealed class Conjunction
        {
            public string Name { get; }
            public Property Property { get; } // Property that differentiates between actual blocks
            public string[] Targets { get; }
            public bool Emitted { get; set; }

            public Conjunction(string name, Property property, params string[] targets)
            {
                Name = name;
                Property = property;
                Targets = targets;
            }
        }
    }
}
