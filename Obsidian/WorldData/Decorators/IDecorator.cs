namespace Obsidian.WorldData.Decorators;

public interface IDecorator
{
    public DecoratorFeatures Features { get; }
    public void Decorate();
}
