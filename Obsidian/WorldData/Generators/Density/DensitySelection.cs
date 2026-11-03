namespace Obsidian.WorldData.Generators.Density;

/// <summary>A branch's volume indices and their destinations in its caller's compact buffer.</summary>
internal readonly ref struct DensitySelection : IDisposable
{
    private readonly ScratchBuffer<int> storage;
    private readonly int capacity;
    private readonly int count;

    public DensitySelection(
        ReadOnlySpan<float> controls,
        ReadOnlySpan<int> parentIndices,
        DensitySelectionKind kind,
        float min = 0.0f,
        float max = 0.0f)
    {
        this.capacity = controls.Length;
        this.storage = ScratchBuffer<int>.Rent(this.capacity * 2);
        var storage = this.storage.Span;
        var count = 0;

        for (var i = 0; i < controls.Length; i++)
        {
            var value = controls[i];
            var selected = kind switch
            {
                DensitySelectionKind.InRange => value >= min && value < max,
                DensitySelectionKind.OutOfRange => !(value >= min && value < max),
                DensitySelectionKind.NotZero => value != 0.0f,
                DensitySelectionKind.NotOne => value != 1.0f,
                DensitySelectionKind.Below => value < max,
                _ => !(value < min)
            };

            if (!selected)
                continue;

            storage[count] = parentIndices.IsEmpty ? i : parentIndices[i];
            storage[this.capacity + count] = i;
            count++;
        }

        this.count = count;
    }

    /// <summary>Evaluates only the selected branch positions and scatters them to their caller's slots.</summary>
    public void Sample(DensityFunction function, Span<float> output, in DensityVolume volume)
    {
        if (this.count == 0)
            return;

        if (this.count == volume.Size)
        {
            function.SampleVolume(output, volume);
            return;
        }

        if (function is ConstantFunction constant)
        {
            foreach (var destination in this.storage.Span.Slice(this.capacity, this.count))
                output[destination] = constant.Value;

            return;
        }

        using var values = new DensityBuffer(this.count);
        var sampled = values.Span;
        var destinations = this.storage.Span.Slice(this.capacity, this.count);
        function.SampleSelected(sampled, volume, this.storage.Span.Slice(0, this.count));

        for (var i = 0; i < destinations.Length; i++)
            output[destinations[i]] = sampled[i];
    }

    public void Dispose() => this.storage.Dispose();
}

internal enum DensitySelectionKind
{
    InRange,
    OutOfRange,
    NotZero,
    NotOne,
    Below,
    NotBelow
}
