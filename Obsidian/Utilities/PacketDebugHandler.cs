using Microsoft.Extensions.Logging;
using Obsidian.Net.Packets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Obsidian.Utilities;

internal class PacketDebugHandler
{
    private static readonly string mainDomain = "Obsidian.Assets";

    private static readonly JsonSerializerOptions packetJsonOptions = new()
    {

    };
    private List<PacketDebugItem> Packets = new();

    public PacketDebugHandler()
    {
        RegisterAsync();
    }

    public async void RegisterAsync()
    {
        using Stream fs = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{mainDomain}.packet_debug.json");

        var list = await fs.FromJsonAsync<List<PacketDebugItem>>(packetJsonOptions);

        Packets = list;

        Globals.PacketLogger.LogDebug($"Successfully add {Packets.Count} packets to debug database...");
    }
    internal string GetPacket(ClientState state, PacketType type, int id)
    {
        try
        {
            var packet=Packets.First(x => x.State == state && x.Id == $"0x{id:X2}" && x.BoundTo == type);
            return packet.AsString();
        }
        catch (Exception ex)
        {
        }

        return new PacketDebugItem { Id = $"0x{id:X2}", BoundTo = type, State = state }.AsString();
    }
}

public class PacketDebugItem
{
    [JsonPropertyName("state"), JsonConverter(typeof(JsonStringEnumConverter))]
    public ClientState? State { get; set; }

    [JsonPropertyName("bound_to"), JsonConverter(typeof(JsonStringEnumConverter))]
    public PacketType BoundTo { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }
}

public enum PacketType
{
    Client,
    Server
}
