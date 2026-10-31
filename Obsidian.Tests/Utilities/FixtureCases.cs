using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests.Utilities;

/// <summary>
/// Replays every case of a captured fixture in one test, so a regression reports all the cases it breaks at once.
/// </summary>
internal static class FixtureCases
{
    /// <summary>
    /// Runs <paramref name="check"/> on every case, then fails once naming each case that threw and why.
    /// </summary>
    public static void AssertAll<T>(IEnumerable<T> cases, Func<T, string> name, Action<T> check)
    {
        var failures = new List<string>();
        var count = 0;
        foreach (var testCase in cases)
        {
            count++;
            try
            {
                check(testCase);
            }
            catch (Exception error)
            {
                failures.Add(Describe(name(testCase), error));
            }
        }

        Fail(failures, count);
    }

    /// <summary>
    /// Awaits <paramref name="check"/> on every case, then fails once naming each case that threw and why.
    /// </summary>
    public static async Task AssertAllAsync<T>(IEnumerable<T> cases, Func<T, string> name, Func<T, Task> check)
    {
        var failures = new List<string>();
        var count = 0;
        foreach (var testCase in cases)
        {
            count++;
            try
            {
                await check(testCase);
            }
            catch (Exception error)
            {
                failures.Add(Describe(name(testCase), error));
            }
        }

        Fail(failures, count);
    }

    private static string Describe(string name, Exception error)
    {
        var message = error.Message.ReplaceLineEndings("\n    ");
        return $"{name}:\n    {message}";
    }

    private static void Fail(List<string> failures, int count)
    {
        Assert.True(count > 0, "The fixture has no cases.");
        if (failures.Count == 0)
            return;

        var header = $"{failures.Count} of {count} cases differ:";
        Assert.Fail(string.Join("\n", failures.Prepend(header)));
    }
}
