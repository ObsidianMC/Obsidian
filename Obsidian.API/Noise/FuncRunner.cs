using SharpNoise.Modules;
using System;

namespace Obsidian.API.Noise
{
    public class FuncRunner : Module
    {
        public Module Source0 { get; set; }

        public Func<Module, double, double, double, double> ConditionFunction { get; set; }

        public FuncRunner() : base(1)
        {

        }

        public override double GetValue(double x, double y, double z)
        {
            return ConditionFunction(Source0, x, y, z);
        }
    }
}
