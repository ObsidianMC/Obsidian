using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Obsidian.WorldData.Generators;

/// <summary>
/// Counts the physical cores the process may run on, which is fewer than <see cref="Environment.ProcessorCount"/> when
/// cores run several hardware threads (SMT).
/// </summary>
internal static partial class PhysicalCores
{
    // GetLogicalProcessorInformationEx's RelationProcessorCore.
    private const int RelationProcessorCore = 0;

    /// <summary>
    /// The physical cores holding at least one of the logical processors the process may run on, or null when that
    /// can't be told for sure: on systems other than Windows and Linux, when the topology can't be read, or when fewer
    /// logical processors are visible than <see cref="Environment.ProcessorCount"/> counts.
    /// </summary>
    public static int? Count()
    {
        try
        {
            if (OperatingSystem.IsLinux())
                return CountOnLinux();

            if (OperatingSystem.IsWindows())
                return CountOnWindows();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or FormatException or OverflowException)
        {
        }

        return null;
    }

    /// <summary>
    /// The CPU IDs of a Linux CPU list, such as <c>0-3,8,10-11</c>, or null when it isn't one.
    /// </summary>
    internal static List<int>? ParseCpuList(string list)
    {
        var cpus = new List<int>();
        foreach (var part in list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var bounds = part.Split('-');
            if (bounds.Length > 2
                || !int.TryParse(bounds[0], out var first)
                || !int.TryParse(bounds[^1], out var last)
                || first < 0
                || last < first)
            {
                return null;
            }

            for (var cpu = first; cpu <= last; cpu++)
                cpus.Add(cpu);
        }

        return cpus.Count > 0 ? cpus : null;
    }

    /// <summary>
    /// The physical cores of <paramref name="cpus"/>, given each logical processor's list of the logical processors
    /// sharing its core (<paramref name="siblings"/>), or null when one of them has none or fewer than
    /// <paramref name="processorCount"/> processors are allowed.
    /// </summary>
    internal static int? CountCores(IReadOnlyCollection<int> cpus, int processorCount, Func<int, string?> siblings)
    {
        // Fewer allowed processors than the runtime counts means some aren't seen: counting cores from the rest would
        // throttle generation, so the caller keeps to the logical count instead.
        if (cpus.Count < processorCount)
            return null;

        var cores = new HashSet<string>();
        foreach (var cpu in cpus)
        {
            if (siblings(cpu) is not string core)
                return null;

            cores.Add(core);
        }

        return cores.Count > 0 ? cores.Count : null;
    }

    // The allowed CPUs come from the scheduler's own list, as Process.ProcessorAffinity only shows CPU IDs below 64.
    private static int? CountOnLinux()
    {
        var allowed = File.ReadLines("/proc/self/status")
            .FirstOrDefault(line => line.StartsWith("Cpus_allowed_list:", StringComparison.Ordinal));
        if (allowed is null || ParseCpuList(allowed["Cpus_allowed_list:".Length..]) is not List<int> cpus)
            return null;

        return CountCores(cpus, Environment.ProcessorCount, cpu =>
        {
            var path = $"/sys/devices/system/cpu/cpu{cpu}/topology/thread_siblings_list";
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        });
    }

    // Each core entry lists the logical processors of the core, as a mask in a processor group.
    private static unsafe int? CountOnWindows()
    {
        using var process = Process.GetCurrentProcess();
        var affinity = (ulong)process.ProcessorAffinity;

        // The mask is that of the process's processor group alone; one spanning groups shows fewer processors.
        if (BitOperations.PopCount(affinity) < Environment.ProcessorCount)
            return null;

        uint length = 0;
        GetLogicalProcessorInformationEx(RelationProcessorCore, null, ref length);
        if (length == 0)
            return null;

        var buffer = new byte[length];
        fixed (byte* start = buffer)
        {
            if (!GetLogicalProcessorInformationEx(RelationProcessorCore, start, ref length))
                return null;
        }

        // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX: Relationship and Size, then PROCESSOR_RELATIONSHIP's Flags,
        // EfficiencyClass, 20 reserved bytes and GroupCount, then its GROUP_AFFINITY entries (Mask, Group, padding).
        var cores = 0;
        for (var offset = 0; offset < length;)
        {
            var entry = buffer.AsSpan(offset);
            var size = BinaryPrimitives.ReadInt32LittleEndian(entry[4..]);
            var groupCount = BinaryPrimitives.ReadUInt16LittleEndian(entry[30..]);
            var mask = BinaryPrimitives.ReadUInt64LittleEndian(entry[32..]);
            var group = BinaryPrimitives.ReadUInt16LittleEndian(entry[40..]);

            // The affinity mask is that of the process's group, so only one group can be told apart.
            if (groupCount != 1 || group != 0)
                return null;

            if ((mask & affinity) != 0)
                cores++;

            offset += size;
        }

        return cores > 0 ? cores : null;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetLogicalProcessorInformationEx(int relationship, byte* buffer, ref uint length);
}
