using System;

namespace Obsidian.WorldData.Generators.Modules
{
    public sealed class Terrace<TIn> : StatefulModule<TIn, float>
    {
        public Module<float> Source { get; init; }
        public float[] ControlPoints { get; init; }
        public bool Inverted { get; init; }

        private Func<TIn, float> sourceFunc;

        protected override void InitializeState()
        {
            ModulesHelper.ThrowIfNull(Source);
            sourceFunc = Source.Compile<TIn>();

            if (ControlPoints is null or { Length: 0 })
                throw new ArgumentException($"{nameof(ControlPoints)} can't be empty or null");
        }

        protected override float GetValue(TIn input)
        {
            float sourceValue = sourceFunc(input);

            int index;
            for (index = 0; index < ControlPoints.Length; index++)
            {
                if (sourceValue < ControlPoints[index])
                    break;
            }

            int index0 = Math.Max(index - 1, 0);
            int index1 = Math.Min(index, ControlPoints.Length - 1);

            if (index0 == index1)
                return ControlPoints[index1];

            float value0 = ControlPoints[index0];
            float value1 = ControlPoints[index1];
            float alpha = (sourceValue - value0) / (value1 - value0);

            if (Inverted)
            {
                alpha = 1f - alpha;
                (value0, value1) = (value1, value0);
            }

            alpha *= alpha;

            return MathHelper.Lerp(value0, value1, alpha);
        }
    }
}
