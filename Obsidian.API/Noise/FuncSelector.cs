﻿using SharpNoise.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.API.Noise
{
    /// <summary>
    /// SharpNoise extension that enables a delegate to determine whether
    /// to utilize one of two input modules.
    /// </summary>
    public class FuncSelector : Module
    {
        public Module Source0 { get; set; }

        public Module Source1 { get; set; }

        /// <summary>
        /// A delegate which returns either 0 or 1 to determine which
        /// source module to use.
        /// </summary>
        public Func<(double, double, double), int> ConditionFunction { get; set; }

        /// <summary>
        /// Ctor.
        /// </summary>
        public FuncSelector() : base(2)
        {

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <returns></returns>
        public override double GetValue(double x, double y, double z)
        {
            int val = ConditionFunction((x, y, z));
            if (val == 0)
            {
                return Source0.GetValue(x, y, z);
            }
            return Source1.GetValue(x, y, z);
        }
    }
}
