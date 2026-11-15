namespace Obsidian.API.Inventory.DataComponents;

/// <summary>A constant or a context int provider key. Evaluation belongs to the consuming gameplay system.</summary>
public readonly record struct ComponentIntValue(int Constant, string? Provider = null)
{
    internal static ComponentIntValue Read(INetStreamReader reader) => reader.ReadBoolean()
        ? new(reader.ReadInt()) : new(0, reader.ReadString());
    internal void Write(INetStreamWriter writer)
    {
        writer.WriteBoolean(this.Provider is null);
        if (this.Provider is string provider) writer.WriteString(provider);
        else writer.WriteInt(this.Constant);
    }
}

/// <summary>A constant or a context float provider key.</summary>
public readonly record struct ComponentFloatValue(float Constant, string? Provider = null)
{
    internal static ComponentFloatValue Read(INetStreamReader reader) => reader.ReadBoolean()
        ? new(reader.ReadSingle()) : new(0, reader.ReadString());
    internal void Write(INetStreamWriter writer)
    {
        writer.WriteBoolean(this.Provider is null);
        if (this.Provider is string provider) writer.WriteString(provider);
        else writer.WriteSingle(this.Constant);
    }
}

/// <summary>Cooking burn time or brewing uses, with the processing speed multiplier.</summary>
public sealed record FuelComponentData(ComponentIntValue Amount, ComponentFloatValue SpeedMultiplier);

/// <summary>The entity types affected by an equipped item's targeting visibility multiplier.</summary>
public sealed record MobVisibilityData(IdSet TargetingEntityTypes, float Visibility);

public static partial class ComponentBuilder
{
    /// <summary>
    /// Gets a new <c>minecraft:additional_trade_cost</c> component: an extra cost added to a trade selling the item.
    /// </summary>
    public static SimpleDataComponent<int> AdditionalTradeCost => BuildSimpleComponent(DataComponentType.AdditionalTradeCost,
        "minecraft:additional_trade_cost", (writer, value) => writer.WriteVarInt(value), reader => reader.ReadVarInt());

    /// <summary>
    /// Gets a new <c>minecraft:villager_food</c> component: how much nutrition the item gives a villager.
    /// </summary>
    public static SimpleDataComponent<int> VillagerFood => BuildSimpleComponent(DataComponentType.VillagerFood,
        "minecraft:villager_food", (writer, value) => writer.WriteVarInt(value), reader => reader.ReadVarInt());

    /// <summary>
    /// Gets a new <c>minecraft:dye</c> component: the color the item dyes things.
    /// </summary>
    public static SimpleDataComponent<Dye> Dye => BuildSimpleComponent(DataComponentType.Dye,
        "minecraft:dye", (writer, value) => writer.WriteVarInt(value), reader => reader.ReadVarInt<Dye>());

    /// <summary>
    /// Gets a new <c>minecraft:cushion/color</c> component: a cushion's color.
    /// </summary>
    public static SimpleDataComponent<Dye> CushionColor => BuildSimpleComponent(DataComponentType.CushionColor,
        "minecraft:cushion/color", (writer, value) => writer.WriteVarInt(value), reader => reader.ReadVarInt<Dye>());

    /// <summary>
    /// Gets a new <c>minecraft:waxed</c> component: a marker that the item is waxed.
    /// </summary>
    public static SimpleDataComponent Waxed => new(DataComponentType.Waxed, "minecraft:waxed");

    /// <summary>
    /// Gets a new <c>minecraft:compostable</c> component: the composter layers the item adds, as a constant or a
    /// provider key.
    /// </summary>
    public static SimpleDataComponent<ComponentIntValue> Compostable => BuildSimpleComponent(DataComponentType.Compostable,
        "minecraft:compostable", (writer, value) => value.Write(writer), ComponentIntValue.Read);

    /// <summary>
    /// Gets a new <c>minecraft:cooking_fuel</c> component: how long the item burns in a furnace and how fast it cooks.
    /// </summary>
    public static SimpleDataComponent<FuelComponentData> CookingFuel => Fuel(DataComponentType.CookingFuel, "minecraft:cooking_fuel");

    /// <summary>
    /// Gets a new <c>minecraft:brewing_fuel</c> component: how many brews the item fuels in a brewing stand and how
    /// fast it brews.
    /// </summary>
    public static SimpleDataComponent<FuelComponentData> BrewingFuel => Fuel(DataComponentType.BrewingFuel, "minecraft:brewing_fuel");

    private static SimpleDataComponent<FuelComponentData> Fuel(DataComponentType type, string name) =>
        BuildSimpleComponent(type, name, (writer, value) =>
        {
            value.Amount.Write(writer);
            value.SpeedMultiplier.Write(writer);
        }, reader => new FuelComponentData(ComponentIntValue.Read(reader), ComponentFloatValue.Read(reader)));

    /// <summary>
    /// Gets a new <c>minecraft:mob_visibility</c> component: how visible the item makes its wearer to the given mobs.
    /// </summary>
    public static SimpleDataComponent<MobVisibilityData> MobVisibility => BuildSimpleComponent(DataComponentType.MobVisibility,
        "minecraft:mob_visibility", (writer, value) =>
        {
            IdSet.Write(value.TargetingEntityTypes, writer, ComponentValueCodecs.EntityTypes);
            writer.WriteSingle(value.Visibility);
        }, reader => new MobVisibilityData(IdSet.Read(reader, ComponentValueCodecs.EntityTypes), reader.ReadSingle()));

    /// <summary>
    /// Gets a new <c>minecraft:sulfur_cube_content</c> component: the stack a sulfur cube holds.
    /// </summary>
    public static SimpleDataComponent<ItemStack> SulfurCubeContent => BuildSimpleComponent(DataComponentType.SulfurCubeContent,
        "minecraft:sulfur_cube_content", (writer, value) => writer.WriteItemStackTemplate(value),
        reader => reader.ReadItemStackTemplate(), value => ComponentCopies.Stack(value)!);
}
