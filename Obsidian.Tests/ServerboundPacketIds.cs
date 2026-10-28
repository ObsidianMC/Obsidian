using Obsidian.Net.Packets;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The client handlers dispatch serverbound packets on the id constants <c>PacketClassesGenerator</c> gives each packet
/// class, so that they follow vanilla's ids when the assets are regenerated for another version.
/// </summary>
public class ServerboundPacketIds
{
    [Fact]
    public void PacketClassesHaveVanillasIds()
    {
        var assembly = typeof(IServerboundPacket).Assembly;

        using var stream = assembly.GetManifestResourceStream("Obsidian.Assets.packets.json")!;
        using var packets = JsonDocument.Parse(stream);

        var mismatches = new List<string>();
        var checkedPackets = 0;
        foreach (var packet in packets.RootElement.EnumerateArray())
        {
            if (packet.GetProperty("namespace").GetString() != "Serverbound")
                continue;

            var state = packet.GetProperty("state").GetString();
            var name = packet.GetProperty("name").GetString();
            var expected = packet.GetProperty("packet_id").GetInt32();

            // Packets shared by several states are one common class with an id for each state.
            var type = assembly.GetType($"Obsidian.Net.Packets.{state}.Serverbound.{name}Packet");
            var constant = type?.GetField("PacketId");
            if (type is null)
            {
                type = assembly.GetType($"Obsidian.Net.Packets.Common.{name}Packet");
                constant = type?.GetField($"Serverbound{state}Id");
            }

            var actual = constant is { IsLiteral: true } && type!.IsAssignableTo(typeof(IServerboundPacket))
                ? (int?)constant.GetRawConstantValue()
                : null;

            if (actual != expected)
                mismatches.Add($"{state} {name}: expected {expected}, got {actual?.ToString() ?? "no constant"}");

            checkedPackets++;
        }

        Assert.Empty(mismatches);
        Assert.True(checkedPackets > 0);
    }
}
