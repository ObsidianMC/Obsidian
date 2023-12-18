﻿using Obsidian.API;
using Obsidian.API.BlockStates.Builders;
using Obsidian.API.Utilities;
using System.Reflection;
using System.Runtime.Remoting;

namespace Obsidian.Nbt.Factory;
internal class BlockStateBuilderFactory
{
    public static IBlockState Builder(NbtCompound comp)
    {
        Type builderType;
        object inst;
        try
        {
            var name = comp.GetString("Name").Split(":")[1].ToPascalCase();
            var assm = Assembly.Load("Obsidian.API");
            builderType = assm.GetType($"Obsidian.API.BlockStates.Builders.{name}StateBuilder");
            inst = Activator.CreateInstance(builderType);
        }
        catch 
        {
            return null;
        }

        if (comp.TryGetTag("Properties", out var props))
        {
            foreach (var prop in props as NbtCompound)
            {
                var propName = prop.Key.ToPascalCase();
                var instProp = builderType.GetProperty(propName);
                var propType = instProp.PropertyType;
                if (propType.IsSubclassOf(typeof(Enum)))
                {
                    object val;
                    if (prop.Value is NbtTag<string> enumVal)
                    {
                        val = Enum.Parse(propType, enumVal.Value.ToPascalCase());
                    }
                    else
                    {
                        return null;
                    }
                    instProp.SetValue(inst, val);
                }
            }
        }

        var thing = (IStateBuilder<IBlockState>)inst;
        return thing.Build();
    }
}
