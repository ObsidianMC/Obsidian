namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public interface IDecorator
{
    public DecoratorFeatures Features { get; }
    void Decorate();
}
