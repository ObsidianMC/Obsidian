using Obsidian.ChunkData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public static class DecoratorFactory
    {
        private static Dictionary<Biomes, Type> DecoratorLookup = new Dictionary<Biomes, Type>();

        public static BaseDecorator GetDecorator(Biomes b)
        {
            if (DecoratorLookup.ContainsKey(b))
            {
                return (BaseDecorator)Activator.CreateInstance(DecoratorLookup[b], b);
            }

            else
            {
                var assembly = Assembly.GetExecutingAssembly();
                Type decoratorType = assembly.GetTypes().FirstOrDefault(t => t.Name == $"{b}Decorator") ?? typeof(DefaultDecorator);
                DecoratorLookup.TryAdd(b, decoratorType);
                return (BaseDecorator)Activator.CreateInstance(decoratorType, b);
            }
            
        }
    }
}
