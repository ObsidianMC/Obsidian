using System.Diagnostics;

namespace Obsidian.API;

[DebuggerDisplay("{Name,nq}:{Id}")]
public readonly struct Block : IEquatable<Block>, IPaletteValue<Block>
{
    public static Block Air => new(0, 0);

    internal static string[] blockNames;
    internal static ushort[] numericToBase;
    internal static ushort[] stateToNumeric;
    internal static ushort[] stateToBase;

    private static ushort[] interactables;
    private static bool initialized = false;

    public string UnlocalizedName => blockNames[Id];
    public string Name => Material.ToString();
    public Material Material => (Material)Id;
    public bool IsInteractable => Array.BinarySearch(interactables, baseId) > -1;
    public bool IsAir => baseId == 0 || baseId == 10547 || baseId == 10546;
    public bool IsFluid => UnlocalizedName == "minecraft:water";
    public bool IsTransparent => Material is Material.Glass || IsFluid || IsAir || Replaceable.Contains(Material);
    public int Id => stateToNumeric[baseId];
    public int StateId => baseId + state;
    public int State => state;
    public int BaseId => baseId;

    private readonly ushort baseId;
    private readonly ushort state;

    internal static readonly List<Material> Replaceable = new()
    {
        Material.Air,
        Material.CaveAir,
        Material.Cobweb,
        Material.SugarCane,
        Material.DeadBush,
        Material.Grass,
        Material.TallGrass,
        Material.AcaciaSapling,
        Material.BambooSapling,
        Material.BirchSapling,
        Material.DarkOakSapling,
        Material.JungleSapling,
        Material.OakSapling,
        Material.SpruceSapling,
        Material.Dandelion,
        Material.Poppy,
        Material.BlueOrchid,
        Material.Allium,
        Material.AzureBluet,
        Material.OrangeTulip,
        Material.PinkTulip,
        Material.RedTulip,
        Material.WhiteTulip,
        Material.OxeyeDaisy,
        Material.Cornflower,
        Material.LilyOfTheValley,
        Material.WitherRose,
        Material.Sunflower,
        Material.Lilac,
        Material.Sunflower,
        Material.Peony,
        Material.Wheat,
        Material.PumpkinStem,
        Material.Carrots,
        Material.Potatoes,
        Material.Beetroots,
        Material.SweetBerryBush
    };

    internal static readonly List<Material> GravityAffected = new()
    {
        Material.Anvil,
        Material.ChippedAnvil,
        Material.DamagedAnvil,
        Material.WhiteConcretePowder,
        Material.OrangeConcretePowder,
        Material.MagentaConcretePowder,
        Material.LightBlueConcretePowder,
        Material.YellowConcretePowder,
        Material.LimeConcretePowder,
        Material.PinkConcretePowder,
        Material.GrayConcretePowder,
        Material.LightGrayConcretePowder,
        Material.CyanConcretePowder,
        Material.PurpleConcretePowder,
        Material.BlueConcretePowder,
        Material.BrownConcretePowder,
        Material.GreenConcretePowder,
        Material.RedConcretePowder,
        Material.BlackConcretePowder,
        Material.DragonEgg,
        Material.RedSand,
        Material.Sand,
        Material.Scaffolding
    };

    internal static readonly List<string> BlockEntityIds = new()
    {
        "minecraft:furnace",
        "minecraft:chest",
        "minecraft:trapped_chest",
        "minecraft:ender_chest",
        "minecraft:jukebox",
        "minecraft:dispenser",
        "minecraft:dropper",
        "minecraft:sign",
        "minecraft:mob_spawner",
        "minecraft:piston",
        "minecraft:brewing_stand",
        "minecraft:enchanting_table",
        "minecraft:end_portal",
        "minecraft:beacon",
        "minecraft:skull",
        "minecraft:daylight_detector",
        "minecraft:hopper",
        "minecraft:comparator",
        "minecraft:banner",
        "minecraft:structure_block",
        "minecraft:end_gateway",
        "minecraft:command_block",
        "minecraft:shulker_box",
        "minecraft:bed",
        "minecraft:conduit",
        "minecraft:barrel",
        "minecraft:smoker",
        "minecraft:blast_furnace",
        "minecraft:lectern",
        "minecraft:bell",
        "minecraft:jigsaw",
        "minecraft:campfire",
        "minecraft:beehive",
        "minecraft:sculk_sensor",
    };

    public Block(int stateId) : this((ushort)stateId)
    {
    }

    public Block(ushort stateId)
    {
        baseId = stateToBase[stateId];
        state = (ushort)(stateId - baseId);
    }

    public Block(int baseId, int state) : this((ushort)baseId, (ushort)state)
    {
    }

    public Block(ushort baseId, ushort state)
    {
        this.baseId = baseId;
        this.state = state;
    }

    public Block(Material material, ushort state = 0)
    {
        baseId = numericToBase[(int)material];
        this.state = state;
    }

    public override string ToString()
    {
        return UnlocalizedName;
    }

    public override int GetHashCode()
    {
        return StateId;
    }

    public bool Is(Material material)
    {
        return stateToNumeric[baseId] == (ushort)material;
    }

    public override bool Equals(object? obj)
    {
        return (obj is Block block) && block.StateId == StateId;
    }

    public bool Equals(Block other)
    {
        return other.StateId == StateId;
    }

    public static bool operator ==(Block a, Block b)
    {
        return a.StateId == b.StateId;
    }

    public static bool operator !=(Block a, Block b)
    {
        return a.StateId != b.StateId;
    }

    internal static void Initialize()
    {
        if (initialized)
            return;
        initialized = true;

        interactables = new[]
        {
            numericToBase[(int)Material.Chest],
            numericToBase[(int)Material.CraftingTable],
            numericToBase[(int)Material.Furnace],
            numericToBase[(int)Material.BrewingStand],
            numericToBase[(int)Material.EnderChest],
            numericToBase[(int)Material.Anvil],
            numericToBase[(int)Material.ChippedAnvil],
            numericToBase[(int)Material.DamagedAnvil],
            numericToBase[(int)Material.TrappedChest],
            numericToBase[(int)Material.Hopper],
            numericToBase[(int)Material.Barrel],
            numericToBase[(int)Material.Smoker],
            numericToBase[(int)Material.BlastFurnace],
            numericToBase[(int)Material.Grindstone],
        };
    }

    public static Block Construct(int value) => new(value);
}
