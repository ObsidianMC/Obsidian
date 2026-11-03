using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
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
    /// The physical cores holding at least one of the logical processors the process's affinity allows, or null when
    /// that can't be told: on systems other than Windows and Linux, with more than 64 logical processors, or when the
    /// topology can't be read.
    /// </summary>
    public static int? Count()
    {
        try
        {
            if (Environment.ProcessorCount > 64)
                return null;

            using var process = Process.GetCurrentProcess();
            if (OperatingSystem.IsWindows())
                return CountOnWindows((ulong)process.ProcessorAffinity);

            if (OperatingSystem.IsLinux())
                return CountOnLinux((ulong)process.ProcessorAffinity);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or FormatException)
        {
        }

        return null;
    }

    // Each core entry lists the logical processors of the core, as a mask in a processor group.
    private static unsafe int? CountOnWindows(ulong affinity)
    {
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

        return cores;
    }

    // Each logical processor lists the logical processors of its core; a core is counted once per distinct list.
    private static int? CountOnLinux(ulong affinity)
    {
        var cores = new HashSet<string>();
        for (var cpu = 0; cpu < 64; cpu++)
        {
            if ((affinity & (1UL << cpu)) == 0)
                continue;

            var siblings = $"/sys/devices/system/cpu/cpu{cpu}/topology/thread_siblings_list";
            if (!File.Exists(siblings))
                return null;

            cores.Add(File.ReadAllText(siblings).Trim());
        }

        return cores.Count > 0 ? cores.Count : null;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetLogicalProcessorInformationEx(int relationship, byte* buffer, ref uint length);
}
