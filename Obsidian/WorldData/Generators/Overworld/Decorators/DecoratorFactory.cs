using Obsidian.ChunkData;
using System;
using System.Linq;
using System.Reflection;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public static class DecoratorFactory
    {
        public static BaseDecorator GetDecorator(Biomes b)
        {
            string biomeName = b.ToString();
            var assembly = Assembly.GetExecutingAssembly();
            Type decoratorType = assembly.GetTypes().FirstOrDefault(t => t.Name == $"{biomeName}Decorator") ?? typeof(DefaultDecorator);
            return (BaseDecorator)Activator.CreateInstance(decoratorType, b);
        }
    }
}
