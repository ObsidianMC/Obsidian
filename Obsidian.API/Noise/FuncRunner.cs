using SharpNoise.Modules;
using System;

namespace Obsidian.API.Noise
{
    public class FuncRunner : Module
    {
        public Module Source0 { get; set; }

        /// <summary>
        /// A delegate which returns either 0 or 1 to determine which
        /// source module to use.
        /// </summary>
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
