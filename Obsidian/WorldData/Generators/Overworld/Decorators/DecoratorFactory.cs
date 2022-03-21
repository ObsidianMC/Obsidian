using System.Reflection;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public static class DecoratorFactory
{
    private static ConcurrentDictionary<Biomes, Type> DecoratorLookup = new ConcurrentDictionary<Biomes, Type>();

    public static BaseDecorator GetDecorator(Biomes b, Chunk chunk, Vector position, GenHelper util)
    {
        if (DecoratorLookup.ContainsKey(b))
        {
            return (BaseDecorator)Activator.CreateInstance(DecoratorLookup[b], b, chunk, position, util);
        }

        else
        {
            var assembly = Assembly.GetExecutingAssembly();
            Type decoratorType = assembly.GetTypes().FirstOrDefault(t => t.Name == $"{b}Decorator") ?? typeof(DefaultDecorator);
            DecoratorLookup.TryAdd(b, decoratorType);
            return (BaseDecorator)Activator.CreateInstance(decoratorType, b, chunk, position, util);
        }

    }
}
