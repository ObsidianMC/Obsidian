namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:add")]
[DensityFunction("minecraft:overworld_large_biomes/sloped_cheese")]
public sealed class AddDensityFunction : IDensityFunction
{
    public string Type => "minecraft:add";

    public required IDensityFunction Argument1 { get; init; }

    public required IDensityFunction Argument2 { get; init; }

    public double MinValue => this.Argument1.MinValue + this.Argument2.MinValue;

    public double MaxValue => this.Argument1.MaxValue + this.Argument2.MaxValue;

    public double GetValue(double x, double y, double z) => this.Argument1.GetValue(x, y, z) + this.Argument2.GetValue(x, y, z);

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new AddDensityFunction
    {
        Argument1 = visitor.Map(this.Argument1),
        Argument2 = visitor.Map(this.Argument2)
    });
}
