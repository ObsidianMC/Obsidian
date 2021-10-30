using SharpNoise.Modules;
using System.Linq;

namespace Obsidian.API.Noise
{
    public class Blur : Module
    {
        /// <summary>
        /// Source to blur. Expensive ops should be cached.
        /// </summary>
        public Module Source0 { get; set; }

        /// <summary>
        /// ctor.
        /// </summary>
        public Blur() : base(1)
        {

        }

        /// <summary>
        /// Perform blur.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <returns></returns>
        public override double GetValue(double x, double y, double z)
        {
            // Bitshift the source module by 1 one so we can
            // "divide" each pixel into 4 quadrants
            var shifted = new BitShiftInput(this.Source0)
            {
                Amount = 1,
                Left = false
            };

            var self = shifted.GetValue(x, y, z);
            var values = new double[8]
            {
                self, // weighted average filter
                self, // center gets a higher weight
                self,
                self, 
                shifted.GetValue(x, y, z + 1),
                shifted.GetValue(x, y, z - 1),
                shifted.GetValue(x + 1, y, z),
                shifted.GetValue(x - 1, y, z),
            };

            return values.Average();
        }
    }
}
