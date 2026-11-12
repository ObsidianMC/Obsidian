using Obsidian.API;
using Obsidian.Registries;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The typed property handles the hot paths use read and set state ids by arithmetic, so they must give the by-name
/// API's answer for every vanilla state.
/// </summary>
public class BlockStatePropertyHandles
{
    private static readonly IntPropertyHandle[] intHandles =
    [
        PropertyHandles.Power,
        PropertyHandles.Age,
        PropertyHandles.Distance,
        PropertyHandles.Level
    ];

    private static readonly BoolPropertyHandle[] boolHandles =
    [
        PropertyHandles.Powered,
        PropertyHandles.Open,
        PropertyHandles.Waterlogged,
        PropertyHandles.Enabled,
        PropertyHandles.Lit
    ];

    private static readonly BlockFace[] faces = Enum.GetValues<BlockFace>();

    [Fact(DisplayName = "Typed property handles read and set every vanilla state like the by-name API")]
    public void HandlesMatchTheByNameApi()
    {
        var mismatches = new List<string>();

        foreach (var handle in intHandles)
            CheckInt(handle, mismatches);

        foreach (var handle in boolHandles)
            CheckBool(handle, mismatches);

        CheckFace(PropertyHandles.Facing, mismatches);

        Assert.True(
            mismatches.Count == 0,
            $"{mismatches.Count} mismatches:\n{string.Join('\n', mismatches.Take(20))}");
    }

    private static void CheckInt(IntPropertyHandle handle, List<string> mismatches)
    {
        var valuesByBlock = ValuesByBlock(handle.Name);

        foreach (var block in AllStates())
        {
            var byName = block.GetProperty(handle.Name);
            var found = handle.TryGet(block, out var value);
            if (byName is null)
            {
                CheckAbsent(block, handle.Name, found, handle.With(block, 0), mismatches);
                continue;
            }

            if (!found || value.ToString(CultureInfo.InvariantCulture) != byName)
                mismatches.Add($"get {handle.Name} of {Describe(block)}: {(found ? value.ToString() : "none")}");

            // Every value the block takes, and one past each end, which leaves the block as it is.
            var values = valuesByBlock[block.RegistryId];
            int[] candidates = [values.Min - 1, .. values, values.Max + 1];
            foreach (var candidate in candidates)
            {
                var expected = block.WithProperty(handle.Name, candidate);
                CheckSet(block, handle.Name, candidate, expected, handle.With(block, candidate), mismatches);
            }
        }
    }

    private static void CheckBool(BoolPropertyHandle handle, List<string> mismatches)
    {
        foreach (var block in AllStates())
        {
            var byName = block.GetProperty(handle.Name);
            var found = handle.TryGet(block, out var value);
            if (byName is null)
            {
                CheckAbsent(block, handle.Name, found || handle.IsTrue(block), handle.With(block, true), mismatches);
                continue;
            }

            if (!found || value != (byName == "true") || handle.IsTrue(block) != value)
                mismatches.Add($"get {handle.Name} of {Describe(block)}: {(found ? value.ToString() : "none")}");

            foreach (var candidate in new[] { true, false })
            {
                var expected = block.WithProperty(handle.Name, candidate);
                CheckSet(block, handle.Name, candidate, expected, handle.With(block, candidate), mismatches);
            }
        }
    }

    private static void CheckFace(FacePropertyHandle handle, List<string> mismatches)
    {
        foreach (var block in AllStates())
        {
            var byName = block.GetProperty(handle.Name);
            var found = handle.TryGet(block, out var value);
            if (byName is null)
            {
                CheckAbsent(block, handle.Name, found, handle.With(block, BlockFace.North), mismatches);
                continue;
            }

            if (!found || !string.Equals(value.ToString(), byName, StringComparison.OrdinalIgnoreCase))
                mismatches.Add($"get {handle.Name} of {Describe(block)}: {(found ? value.ToString() : "none")}");

            // All six faces, including the ones the block can't face, which leave it as it is.
            foreach (var candidate in faces)
            {
                var expected = block.WithProperty(handle.Name, candidate.ToString().ToLowerInvariant());
                CheckSet(block, handle.Name, candidate, expected, handle.With(block, candidate), mismatches);
            }
        }
    }

    // The by-name API has no value for a property the block doesn't have, and returns the block itself when told to
    // set it.
    private static void CheckAbsent(IBlock block, string name, bool found, IBlock set, List<string> mismatches)
    {
        if (found)
            mismatches.Add($"get {name} of {Describe(block)}: a value where by name it has none");

        if (!ReferenceEquals(set, block))
            mismatches.Add($"set {name} of {Describe(block)}: {Describe(set)} where by name it's unchanged");
    }

    private static void CheckSet<T>(IBlock block, string name, T value, IBlock expected, IBlock actual,
        List<string> mismatches)
    {
        var sameState = expected.GetHashCode() == actual.GetHashCode();
        var sameInstance = ReferenceEquals(expected, block) == ReferenceEquals(actual, block);
        if (!sameState || !sameInstance)
            mismatches.Add($"set {name}={value} on {Describe(block)}: {Describe(actual)}, by name {Describe(expected)}");
    }

    // Every value of an integer property, by the registry id of each block that has it, read by name.
    private static Dictionary<int, SortedSet<int>> ValuesByBlock(string property)
    {
        var values = new Dictionary<int, SortedSet<int>>();
        foreach (var block in AllStates())
        {
            if (block.GetProperty(property) is not string value)
                continue;

            if (!values.TryGetValue(block.RegistryId, out var set))
            {
                set = [];
                values[block.RegistryId] = set;
            }

            set.Add(int.Parse(value, CultureInfo.InvariantCulture));
        }

        return values;
    }

    private static IEnumerable<IBlock> AllStates() =>
        Enumerable.Range(0, BlocksRegistry.StateToNumeric.Length).Select(BlocksRegistry.Get);

    private static string Describe(IBlock block) => $"{block.UnlocalizedName} ({block.GetHashCode()})";
}
