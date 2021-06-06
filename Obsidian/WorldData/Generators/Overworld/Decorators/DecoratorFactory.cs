using Obsidian.ChunkData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public static class DecoratorFactory
    {
        private static Dictionary<string, Type> DecoratorLookup = new Dictionary<string, Type>();

        public static BaseDecorator GetDecorator(Biomes b)
        {
            string biomeName = b.ToString();
            if (DecoratorLookup.ContainsKey(biomeName))
            {
                return (BaseDecorator)Activator.CreateInstance(DecoratorLookup[biomeName]);
            }

            else
            {
                var assembly = Assembly.GetExecutingAssembly();
                Type decoratorType = assembly.GetTypes().FirstOrDefault(t => t.Name == $"{biomeName}Decorator") ?? typeof(DefaultDecorator);
                DecoratorLookup.TryAdd(biomeName, decoratorType);
                return (BaseDecorator)Activator.CreateInstance(decoratorType, b);
            }
            
        }
    }
}
