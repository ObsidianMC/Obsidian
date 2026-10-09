using Obsidian.API.Noise;
using Obsidian.API.World.Generator.DensityFunctions;
using Obsidian.API.World.Generator.Noise;
using System.Runtime.Intrinsics;

namespace Obsidian.WorldData.Generators.Mojang;

internal sealed partial class NoiseChunk
{
    // Column fillers of the mapped functions, so functions shared by the interpolators share a filler and its cache.
    private readonly Dictionary<IDensityFunction, ColumnFiller> columnFillers = new(ReferenceEqualityComparer.Instance);

    // Every corner Y index of a column, in order.
    private int[]? allCornerIndices;

    /// <summary>
    /// Samples <paramref name="filler"/> at every corner of the column at (<paramref name="blockX"/>, <paramref name="blockZ"/>).
    /// </summary>
    private void FillCornerColumn(ColumnFiller filler, int blockX, int blockZ, Span<double> values)
    {
        var indices = this.allCornerIndices ??= [.. Enumerable.Range(0, this.CellCountY + 1)];
        filler.Fill(new CornerColumn(blockX, blockZ, this.CellNoiseMinY * this.CellHeight, this.CellHeight), indices, values);
    }

    /// <summary>
    /// The filler sampling a mapped function at the corners of a column, or <c>null</c> where noises can't be sampled in
    /// lanes, which is what makes filling a column worth it.
    /// </summary>
    private ColumnFiller? CompileForColumns(IDensityFunction function) =>
        ImprovedNoiseLanes.IsSupported ? this.CompileColumn(function) : null;

    private ColumnFiller CompileColumn(IDensityFunction function)
    {
        if (this.columnFillers.TryGetValue(function, out var filler))
            return filler;

        filler = function switch
        {
            ConstantDensityFunction constant => new ConstantColumn(constant.Argument),
            // Their values don't depend on Y; asking them at the first corner also keeps their caches as sampling does.
            FlatCache flatCache => new FlatCacheColumn(flatCache),
            ColumnCache or Cache2D => new YIndependentColumn(function),
            CacheOnce cacheOnce => new CachedColumn(this.CompileColumn(cacheOnce.Argument), this.CellCountY + 1),
            BlendDensityFunction blend => this.CompileColumn(blend.Argument),
            AddDensityFunction add => new BinaryColumn(BinaryOperation.Add, this.CompileColumn(add.Argument1), this.CompileColumn(add.Argument2),
                0.0, this.CellCountY + 1),
            MulDensityFunction mul => new BinaryColumn(BinaryOperation.Mul, this.CompileColumn(mul.Argument1), this.CompileColumn(mul.Argument2),
                0.0, this.CellCountY + 1),
            MinDensityFunction min => new BinaryColumn(BinaryOperation.Min, this.CompileColumn(min.Argument1), this.CompileColumn(min.Argument2),
                min.Argument2.MinValue, this.CellCountY + 1),
            MaxDensityFunction max => new BinaryColumn(BinaryOperation.Max, this.CompileColumn(max.Argument1), this.CompileColumn(max.Argument2),
                max.Argument2.MaxValue, this.CellCountY + 1),
            AbsDensityFunction abs => new UnaryColumn(UnaryOperation.Abs, this.CompileColumn(abs.Argument)),
            SquareDensityFunction square => new UnaryColumn(UnaryOperation.Square, this.CompileColumn(square.Argument)),
            CubeDensityFunction cube => new UnaryColumn(UnaryOperation.Cube, this.CompileColumn(cube.Argument)),
            HalfNegativeDensityFunction halfNegative => new UnaryColumn(UnaryOperation.HalfNegative, this.CompileColumn(halfNegative.Argument)),
            QuarterNegativeDensityFunction quarterNegative =>
                new UnaryColumn(UnaryOperation.QuarterNegative, this.CompileColumn(quarterNegative.Argument)),
            SqueezeDensityFunction squeeze => new UnaryColumn(UnaryOperation.Squeeze, this.CompileColumn(squeeze.Argument)),
            InvertDensityFunction invert => new UnaryColumn(UnaryOperation.Invert, this.CompileColumn(invert.Argument)),
            ClampDensityFunction clamp => new UnaryColumn(UnaryOperation.Clamp, this.CompileColumn(clamp.Input), clamp.Min, clamp.Max),
            RangeChoiceDensityFunction rangeChoice => new RangeChoiceColumn(rangeChoice.MinInclusive, rangeChoice.MaxExclusive,
                this.CompileColumn(rangeChoice.Input), this.CompileColumn(rangeChoice.WhenInRange), this.CompileColumn(rangeChoice.WhenOutOfRange),
                this.CellCountY + 1),
            NoiseDensityFunction { Noise: BaseNoise noise } sampled => new NoiseColumn(noise.NormalNoise, sampled.XzScale, sampled.YScale),
            ShiftedNoiseDensityFunction { Noise: BaseNoise noise } shifted => new ShiftedNoiseColumn(noise.NormalNoise, shifted.XzScale,
                shifted.YScale, this.CompileColumn(shifted.ShiftX), this.CompileColumn(shifted.ShiftY), this.CompileColumn(shifted.ShiftZ),
                this.CellCountY + 1),
            WeirdScaledSamplerDensityFunction { Noise: BaseNoise noise } weird => new WeirdScaledColumn(noise.NormalNoise,
                weird.RarityValueMapper == "type_1", this.CompileColumn(weird.Input), this.CellCountY + 1),
            OldBlendedNoiseDensityFunction blended => new BlendedNoiseColumn(blended.BlendedNoise),
            _ => new PointColumn(function)
        };

        this.columnFillers[function] = filler;
        return filler;
    }

    /// <summary>
    /// A column of cell corners: the corners with index <c>i</c> are at Y <c>MinY + i * StepY</c>.
    /// </summary>
    private readonly record struct CornerColumn(int X, int Z, int MinY, int StepY)
    {
        public int Y(int index) => this.MinY + index * this.StepY;
    }

    /// <summary>
    /// A chunk-bound density function compiled to sample some corners of a column at once: each node is visited once per
    /// column rather than once per corner, and noises sample four corners at a time.
    /// </summary>
    /// <remarks>
    /// The values are the same as <see cref="IDensityFunction.GetValue"/> at each corner. Every node does the same arithmetic
    /// in the same order, and arguments a node skips at a corner (like a multiplication's second argument when the first
    /// is zero) aren't sampled there either, so caches see the same positions first.
    /// </remarks>
    private abstract class ColumnFiller
    {
        /// <summary>
        /// Writes the function's value at the corners of <paramref name="column"/> listed in <paramref name="indices"/>, in
        /// ascending order, to the same places of <paramref name="values"/>.
        /// </summary>
        public abstract void Fill(in CornerColumn column, ReadOnlySpan<int> indices, Span<double> values);
    }

    private sealed class ConstantColumn(double value) : ColumnFiller
    {
        public override void Fill(in CornerColumn column, ReadOnlySpan<int> indices, Span<double> values) => values[..indices.Length].Fill(value);
    }

    /// <summary>
    /// Any other function, sampled corner by corner.
    /// </summary>
    private sealed class PointColumn(IDensityFunction function) : ColumnFiller
    {
        public override void Fill(in CornerColumn column, ReadOnlySpan<int> indices, Span<double> values)
        {
            for (var i = 0; i < indices.Length; i++)
                values[i] = function.GetValue(column.X, column.Y(indices[i]), column.Z);
        }
    }

    /// <summary>
    /// A function whose value doesn't depend on Y, sampled at the first corner.
    /// </summary>
    private sealed class YIndependentColumn(IDensityFunction function) : ColumnFiller
    {
        public override void Fill(in CornerColumn column, ReadOnlySpan<int> indices, Span<double> values)
        {
            if (indices.Length > 0)
                values[..indices.Length].Fill(function.GetValue(column.X, column.Y(indices[0]), column.Z));
        }
    }

    /// <summary>
    /// A flat cache, whose cached columns don't depend on Y.
    /// </summary>
    private sealed class FlatCacheColumn(FlatCache flatCache) : ColumnFiller
    {
        private readonly YIndependentColumn cached = new(flatCache);
        private readonly PointColumn sampled = new(flatCache);

        public override void Fill(in CornerColumn column, ReadOnlySpan<int> indices, Span<double> values)
        {
            if (flatCache.Covers(column.X, column.Z))
                this.cached.Fill(column, indices, values);
            else
                this.sampled.Fill(column, indices, values);
        }
    }

    /// <summary>
    /// Remembers the column's corners sampled so far, for functions several nodes share.
    /// </summary>
    private sealed class CachedColumn(ColumnFiller argument, int cornerCount) : ColumnFiller
    {
        private readonly double[] cached = new double[cornerCount];
        private readonly bool[] sampled = new bool[cornerCount];
        private readonly int[] missing = new int[cornerCount];
        private readonly double[] missingValues = new double[cornerCount];
        private int columnX = int.MinValue;
        private int columnZ;

        public override void Fill(in CornerColumn column, ReadOnlySpan<int> indices, Span<double> values)
        {
            if (column.X != this.columnX || column.Z != this.columnZ)
            {
                Array.Clear(this.sampled);
                (this.columnX, this.columnZ) = (column.X, column.Z);
            }

            var missingCount = 0;
            foreach (var index in indices)
            {
                if (!this.sampled[index])
                    this.missing[missingCount++] = index;
            }

            if (missingCount > 0)
            {
                argument.Fill(column, this.missing.AsSpan(0, missingCount), this.missingValues);
                for (var i = 0; i < missingCount; i++)
                {
                    this.cached[this.missing[i]] = this.missingValues[i];
                    this.sampled[this.missing[i]] = true;
                }
            }

            for (var i = 0; i < indices.Length; i++)
                values[i] = this.cached[indices[i]];
        }
    }

    private sealed class UnaryColumn(UnaryOperation operation, ColumnFiller argument, double min = 0.0, double max = 0.0) : ColumnFiller
    {
        public override void Fill(in CornerColumn column, ReadOnlySpan<int> indices, Span<double> values)
        {
            argument.Fill(column, indices, values);

            for (var i = 0; i < indices.Length; i++)
            {
                var value = values[i];
                values[i] = operation switch
                {
                    UnaryOperation.Abs => Math.Abs(value),
                    UnaryOperation.Square => SquareDensityFunction.Square(value),
                    UnaryOperation.Cube => CubeDensityFunction.Cube(value),
                    UnaryOperation.HalfNegative => HalfNegativeDensityFunction.Transform(value),
                    UnaryOperation.QuarterNegative => QuarterNegativeDensityFunction.Transform(value),
                    UnaryOperation.Squeeze => SqueezeDensityFunction.Transform(value),
                    UnaryOperation.Invert => 1.0 / value,
                    _ => Math.Clamp(value, min, max)
                };
            }
        }
    }

    /// <param name="bound">The second argument's minimum for <see cref="BinaryOperation.Min"/> and maximum for
    /// <see cref="BinaryOperation.Max"/>, which decide when the functions skip it.</param>
    private sealed class BinaryColumn(BinaryOperation operation, ColumnFiller first, ColumnFiller second, double bound, int cornerCount)
        : ColumnFiller
    {
        private readonly int[] needed = new int[cornerCount];
        private readonly int[] neededAt = new int[cornerCount];
        private readonly double[] secondValues = new double[cornerCount];

        public override void Fill(in CornerColumn column, ReadOnlySpan<int> indices, Span<double> values)
        {
            first.Fill(column, indices, values);

            if (operation == BinaryOperation.Add)
            {
                second.Fill(column, indices, this.secondValues);
                for (var i = 0; i < indices.Length; i++)
                    values[i] += this.secondValues[i];

                return;
            }

            // The corners where the function samples its second argument.
            var count = 0;
            for (var i = 0; i < indices.Length; i++)
            {
                var value = values[i];
                var skips = operation switch
                {
                    BinaryOperation.Mul => value == 0.0,
                    BinaryOperation.Min => value < bound,
                    _ => value > bound
                };

                if (skips)
                {
                    // Mul gives 0 for any zero, like the function.
                    if (operation == BinaryOperation.Mul)
                        values[i] = 0.0;

                    continue;
                }

                this.needed[count] = indices[i];
                this.neededAt[count++] = i;
            }

            if (count == 0)
                return;

            second.Fill(column, this.needed.AsSpan(0, count), this.secondValues);
            for (var j = 0; j < count; j++)
            {
                var i = this.neededAt[j];
                values[i] = operation switch
                {
                    BinaryOperation.Mul => values[i] * this.secondValues[j],
                    BinaryOperation.Min => Math.Min(values[i], this.secondValues[j]),
                    _ => Math.Max(values[i], this.secondValues[j])
                };
            }
        }
    }

    private sealed class RangeChoiceColumn(double minInclusive, double maxExclusive, ColumnFiller input, ColumnFiller whenInRange,
        ColumnFiller whenOutOfRange, int cornerCount) : ColumnFiller
    {
        private readonly double[] inputValues = new double[cornerCount];
        private readonly int[] inRange = new int[cornerCount];
        private readonly int[] inRangeAt = new int[cornerCount];
        private readonly int[] outOfRange = new int[cornerCount];
        private readonly int[] outOfRangeAt = new int[cornerCount];
        private readonly double[] branchValues = new double[cornerCount];

        public override void Fill(in CornerColumn column, ReadOnlySpan<int> indices, Span<double> values)
        {
            input.Fill(column, indices, this.inputValues);

            int inCount = 0, outCount = 0;
            for (var i = 0; i < indices.Length; i++)
            {
                var control = this.inputValues[i];
                if (control >= minInclusive && control < maxExclusive)
                {
                    this.inRange[inCount] = indices[i];
                    this.inRangeAt[inCount++] = i;
                }
                else
                {
                    this.outOfRange[outCount] = indices[i];
                    this.outOfRangeAt[outCount++] = i;
                }
            }

            this.FillBranch(whenInRange, column, inCount, this.inRange, this.inRangeAt, values);
            this.FillBranch(whenOutOfRange, column, outCount, this.outOfRange, this.outOfRangeAt, values);
        }

        private void FillBranch(ColumnFiller branch, in CornerColumn column, int count, int[] branchIndices, int[] at, Span<double> values)
        {
            if (count == 0)
                return;

            branch.Fill(column, branchIndices.AsSpan(0, count), this.branchValues);
            for (var j = 0; j < count; j++)
                values[at[j]] = this.branchValues[j];
        }
    }

    /// <summary>
    /// Fills corners four at a time, padding the last group with copies of its last corner, whose values are dropped.
    /// </summary>
    private abstract class LanesColumn : ColumnFiller
    {
        public override void Fill(in CornerColumn column, ReadOnlySpan<int> indices, Span<double> values)
        {
            Span<double> lanes = stackalloc double[4];
            for (var start = 0; start < indices.Length; start += 4)
            {
                var count = Math.Min(4, indices.Length - start);
                var y = Vector256.Create(
                    (double)column.Y(indices[start]), column.Y(indices[start + Math.Min(1, count - 1)]),
                    column.Y(indices[start + Math.Min(2, count - 1)]), column.Y(indices[start + Math.Min(3, count - 1)]));

                this.Sample(column, start, count, y).CopyTo(lanes);
                lanes[..count].CopyTo(values[start..]);
            }
        }

        /// <summary>
        /// The values at corners <paramref name="start"/> to <paramref name="start"/> + <paramref name="count"/> of the
        /// filled ones, at Y <paramref name="y"/>.
        /// </summary>
        protected abstract Vector256<double> Sample(in CornerColumn column, int start, int count, Vector256<double> y);

        /// <summary>
        /// Four values of a buffer from <paramref name="start"/>, padded with the last value like the Ys.
        /// </summary>
        protected static Vector256<double> Lanes(double[] values, int start, int count) => Vector256.Create(values[start],
            values[start + Math.Min(1, count - 1)], values[start + Math.Min(2, count - 1)], values[start + Math.Min(3, count - 1)]);
    }

    // NoiseDensityFunction.GetValue.
    private sealed class NoiseColumn(NormalNoise noise, double xzScale, double yScale) : LanesColumn
    {
        protected override Vector256<double> Sample(in CornerColumn column, int start, int count, Vector256<double> y) =>
            noise.GetValue(Vector256.Create(column.X * xzScale), y * yScale, Vector256.Create(column.Z * xzScale));
    }

    // ShiftedNoiseDensityFunction.GetValue.
    private sealed class ShiftedNoiseColumn(NormalNoise noise, double xzScale, double yScale, ColumnFiller shiftX, ColumnFiller shiftY,
        ColumnFiller shiftZ, int cornerCount) : LanesColumn
    {
        private readonly double[] shiftsX = new double[cornerCount];
        private readonly double[] shiftsY = new double[cornerCount];
        private readonly double[] shiftsZ = new double[cornerCount];

        public override void Fill(in CornerColumn column, ReadOnlySpan<int> indices, Span<double> values)
        {
            shiftX.Fill(column, indices, this.shiftsX);
            shiftY.Fill(column, indices, this.shiftsY);
            shiftZ.Fill(column, indices, this.shiftsZ);
            base.Fill(column, indices, values);
        }

        protected override Vector256<double> Sample(in CornerColumn column, int start, int count, Vector256<double> y) => noise.GetValue(
            Vector256.Create(column.X * xzScale) + Lanes(this.shiftsX, start, count),
            y * yScale + Lanes(this.shiftsY, start, count),
            Vector256.Create(column.Z * xzScale) + Lanes(this.shiftsZ, start, count));
    }

    // WeirdScaledSamplerDensityFunction.GetValue.
    private sealed class WeirdScaledColumn(NormalNoise noise, bool type1, ColumnFiller input, int cornerCount) : LanesColumn
    {
        private readonly double[] rarities = new double[cornerCount];

        public override void Fill(in CornerColumn column, ReadOnlySpan<int> indices, Span<double> values)
        {
            input.Fill(column, indices, this.rarities);
            for (var i = 0; i < indices.Length; i++)
                this.rarities[i] = type1 ? MathUtils.RarityType1(this.rarities[i]) : MathUtils.RarityType2(this.rarities[i]);

            base.Fill(column, indices, values);
        }

        protected override Vector256<double> Sample(in CornerColumn column, int start, int count, Vector256<double> y)
        {
            var rarity = Lanes(this.rarities, start, count);
            var sample = noise.GetValue(Vector256.Create((double)column.X) / rarity, y / rarity, Vector256.Create((double)column.Z) / rarity);
            return rarity * Vector256.Abs(sample);
        }
    }

    // OldBlendedNoiseDensityFunction.GetValue, at whole block positions.
    private sealed class BlendedNoiseColumn(BlendedNoise noise) : LanesColumn
    {
        protected override Vector256<double> Sample(in CornerColumn column, int start, int count, Vector256<double> y) =>
            noise.Compute(Vector256.Create((double)column.X), y, Vector256.Create((double)column.Z));
    }
}
