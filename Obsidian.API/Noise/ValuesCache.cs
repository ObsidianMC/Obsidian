using SharpNoise.Modules;
using System.Collections.Concurrent;

namespace Obsidian.API.Noise;

internal class ValuesCache : Module
{
    public ConcurrentDictionary<(double, double, double), double> Values { get; private set; } = new();

    public Module Source0 { get; set; }

    private int hits, misses = 0;
    
    public ValuesCache() : base(1)
    {
    }

    public override double GetValue(double x, double y, double z)
    {
        var index = (x, y, z);
        if (Values.ContainsKey(index))
        {
            if (Values.TryGetValue(index, out var value))
            {
                hits++;
                return value;
            }
        }

        var val = Source0.GetValue(x, y, z);
        Values.TryAdd(index, val);
        misses++;
        return val;
    }

    internal int GetHitRate()
    {
        return hits / (hits + misses);
    }
}
