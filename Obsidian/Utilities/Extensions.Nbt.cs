using Obsidian.API.Inventory;
using Obsidian.Nbt;
using System.Reflection;

namespace Obsidian.Utilities;

//TODO MAKE NBT DE/SERIALIZERS PLEASE
public partial class Extensions
{
    public static void WriteNbtCompound(this INetStreamWriter writer, NbtCompound compound)
    {
        using var nbtWriter = new RawNbtWriter(true);

        foreach (var (_, tag) in compound)
            nbtWriter.WriteTag(tag);

        nbtWriter.TryFinish();

        writer.WriteByteArray(nbtWriter.Data);
    }

    //DESERIALIZE ITEM COMPONENTS
    public static ItemStack? ItemFromNbt(this NbtCompound? item)
    {
        if (item is null)
            return null;

        var itemStack = ItemsRegistry.GetSingleItem(item.GetString("id"));

        return itemStack;
    }

    //TODO this can be made A LOT FASTER
    public static IBlock ToBlock(this NbtCompound comp)
    {
        var name = comp.GetString("Name").Split(":")[1].ToPascalCase();
        Type builderType = typeof(IBlockState).Assembly.GetType($"Obsidian.API.BlockStates.Builders.{name}StateBuilder");

        if (builderType == null)
        {
            return BlocksRegistry.Get(comp.GetString("Name"));
        }
        var inst = Activator.CreateInstance(builderType);

        if (comp.TryGetTag("Properties", out var props))
        {
            foreach (var prop in props as NbtCompound)
            {
                var instProp = builderType.GetProperty(prop.Key.ToPascalCase());
                Type propType = instProp.PropertyType;
                if (propType.IsSubclassOf(typeof(Enum)))
                {
                    if (prop.Value is NbtTag<string> enumVal && Enum.TryParse(propType, enumVal.Value.ToPascalCase(), out var val))
                        instProp.SetValue(inst, val);
                }
                else if (propType.Name == "Boolean")
                {
                    if (prop.Value is NbtTag<string> boolVal && bool.TryParse(boolVal.Value, out var val))
                        instProp.SetValue(inst, val);
                }
                else if (propType.Name == "Int32")
                {
                    if (prop.Value is NbtTag<string> numVal && int.TryParse(numVal.Value, out var val))
                        instProp.SetValue(inst, val);
                }
            }
        }

        MethodInfo buildMeth = builderType.GetMethod("Build");
        var bs = (IBlockState)buildMeth.Invoke(inst, null);
        var n = comp.GetString("Name");
        return BlocksRegistry.Get(n, bs);
    }
}
