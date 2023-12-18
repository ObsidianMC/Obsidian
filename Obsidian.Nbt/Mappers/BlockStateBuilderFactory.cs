using Microsoft.Win32;
using Obsidian.API;
using Obsidian.API.Utilities;
using System.Diagnostics;
using System.Reflection;

namespace Obsidian.Nbt.Mappers;
internal class BlockStateBuilderMapper
{
    public static IBlockState GetFromNbt(NbtCompound comp)
    {
        Type builderType;
        try
        {
            var name = comp.GetString("Name").Split(":")[1].ToPascalCase();
            var assm = Assembly.Load("Obsidian.API");
            builderType = assm.GetType($"Obsidian.API.BlockStates.Builders.{name}StateBuilder");
        }
        catch 
        {
            throw new NotImplementedException($"Failed to find a block state builder for {comp}");
        }
        if ( builderType == null )
        {
            return null;
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
                    else
                        Debugger.Break();
                }
                else if (propType.Name == "Boolean")
                {
                    if (prop.Value is NbtTag<string> boolVal && bool.TryParse(boolVal.Value, out var val))
                        instProp.SetValue(inst, val);
                    else
                        Debugger.Break();
                }
                else if (propType.Name == "Int32")
                {
                    if (prop.Value is NbtTag<string> numVal && int.TryParse(numVal.Value, out var val))
                        instProp.SetValue(inst, val);
                    else
                        Debugger.Break();
                }
                else
                {
                    // Prop type unfamiliar with
                    Debugger.Break();
                }
            }
        }

        MethodInfo buildMeth = builderType.GetMethod("Build");
        return (IBlockState)buildMeth.Invoke(inst, null);
    }
}
