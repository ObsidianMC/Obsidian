using SharpNoise.Modules;

namespace Obsidian.API.Noise;

public class FuncRunner : Module
{
    public Module Source0 { get; set; }

    public Func<Module, double, double, double, double> ConditionFunction { get; set; }

    public int Utility { get; set; } = 0;

    public FuncRunner(Module source0, Func<Module, double, double, double, double> conditionFunction) : base(1)
    {
        Source0 = source0;
        ConditionFunction = conditionFunction;
    }

    public override double GetValue(double x, double y, double z)
    {
        return ConditionFunction(Source0, x, y, z);
    }
}
