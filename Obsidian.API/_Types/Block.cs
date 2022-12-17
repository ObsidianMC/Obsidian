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

    private static bool initialized = false;

    public string UnlocalizedName => blockNames[Id];
    public string Name => Material.ToString();
    public Material Material => (Material)Id;
    public bool IsAir => baseId == 0 || baseId == 10547 || baseId == 10546;
    public bool IsFluid => UnlocalizedName == "minecraft:water" || UnlocalizedName == "minecraft:lava";
    public bool IsTransparent => Material is Material.Glass || IsFluid || IsAir;
    public int Id => stateToNumeric[baseId];
    public int StateId => baseId + state;
    public int State => state;
    public int BaseId => baseId;

    private readonly ushort baseId;
    private readonly ushort state;

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

    public static Block Construct(int value) => new(value);
}
