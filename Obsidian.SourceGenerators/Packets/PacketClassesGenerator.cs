using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Obsidian.SourceGenerators.Packets;

[Generator]
public sealed class PacketClassesGenerator : IIncrementalGenerator
{
    private static readonly string[] commonPacketNames = [
        "custom_payload",
        "custom_report_details",
        "disconnect",
        "keep_alive",
        "ping",
        "resource_pack_pop",
        "resource_pack_push",
        "server_links",
        "store_cookie",
        "transfer",
        "update_tags",
        "client_information",
        "custom_payload",
        "keep_alive",
        "pong",
        "resource_pack"
    ];

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        //if (!Debugger.IsAttached)
        //    Debugger.Launch();

        var jsonFiles = context.AdditionalTextsProvider
          .Where(file => file.Path.EndsWith(".json"))
          .Select(static (file, ct) => (name: Path.GetFileNameWithoutExtension(file.Path), content: file.GetText(ct)!.ToString()));

        var compilation = context.CompilationProvider.Combine(jsonFiles.Collect());

        context.RegisterSourceOutput(compilation, Generate);
    }

    private void Generate(SourceProductionContext context, (Compilation compilation, ImmutableArray<(string name, string json)> files) output)
    {
        var assembly = output.compilation.AssemblyName;

        if (assembly == "Obsidian")
        {
            var packetsJson = output.files.GetJsonFromArray("packets");

            try
            {
                GeneratePacketClasses(context, output.compilation, packetsJson);
            }
            catch (Exception ex)
            {
                throw ex;
            }

        }
    }

    private void GeneratePacketClasses(SourceProductionContext context, Compilation compilation, string packetsJson)
    {
        var packets = GetPackets(packetsJson);

        var source = new CodeBuilder();

        var commonPackets = new List<Packet>();
        foreach (var packet in packets)
        {
            if (commonPacketNames.Contains(packet.ResourceId))
            {
                commonPackets.Add(packet);
                continue;
            }

            source.Using("Obsidian.Net");
            source.Using("Obsidian.Utilities");
            source.Using("System.Runtime.CompilerServices");
            source.Line();

            source.Namespace($"Obsidian.Net.Packets.{packet.State}.{packet.Namespace}");
            source.Line();

            source.Type($"public partial class {packet.Name}Packet : {packet.UsableInterface}");

            source.Line($"public int Id => {packet.PacketId};");

            if (packet.UsableInterface == Vocabulary.ClientboundInterface)
            {
                source.Method("public void Serialize(MinecraftStream stream)");
                source.EndScope();

                source.Line();
            }
            else if (packet.UsableInterface == Vocabulary.ServerboundInterface)
            {
                source.Method("public void Populate(byte[] data)");
                source.EndScope();

                source.Method("public void Populate(MinecraftStream stream)");
                source.EndScope();

                source.Method("public ValueTask HandleAsync(Server server, Player player)");
                source.Line("return default;");
                source.EndScope();

                source.Line();

                source.Method($"public static {packet.Name}Packet Deserialize(byte[] data)");
                source.Line("throw new NotImplementedException();");
                source.EndScope();

                source.Line();

                source.Method($"public static {packet.Name}Packet Deserialize(MinecraftStream stream)");
                source.Line("throw new NotImplementedException();");
                source.EndScope();
            }

            source.EndScope();

            context.AddSource($"{packet.State}.{packet.Namespace}{packet.Name}.g.cs", source.ToString());

            source.Clear();
        }

        foreach (var groupedPackets in commonPackets.GroupBy(x => x.ResourceId))
        {
            var key = groupedPackets.Key;
            var defaultPacket = groupedPackets.First();
            var dualStream = groupedPackets.Count(x => x.Namespace is Vocabulary.Clientbound or Vocabulary.Serverbound) > 1;
            var packetClassName = $"{defaultPacket.Name}Packet";
            var @interface = dualStream ? $"{Vocabulary.ClientboundInterface}, {Vocabulary.ServerboundInterface}" : defaultPacket.UsableInterface;

            source.Using("Obsidian.Net");
            source.Using("Obsidian.Utilities");
            source.Using("System.Runtime.CompilerServices");
            source.Line();

            source.Namespace($"Obsidian.Net.Packets.Common");
            source.Line();

            source.Type($"public partial record class {packetClassName} : {@interface}");

            foreach (var packet in groupedPackets.GroupBy(x => x.State))
            {
                var state = packet.Key;

                foreach (var value in packet)
                {
                    source.Type($"public static {packetClassName} {value.Namespace}{state} => new()");

                    source.Line($"Id = {value.PacketId}");

                    source.EndScope();

                    source.Line();
                }
            }

            source.Line("public int Id { get; init; }");
            source.Line();

            if (dualStream)
            {
                source.Method("public void Serialize(MinecraftStream stream)");
                source.EndScope();

                source.Line();

                source.Method("public void Populate(byte[] data)");
                source.EndScope();

                source.Line();

                source.Method("public void Populate(MinecraftStream stream)");
                source.EndScope();

                source.Line();

                source.Method("public ValueTask HandleAsync(Server server, Player player)");
                source.Line("return default;");
                source.EndScope();

                source.Line();

                source.Method($"public static {packetClassName} Deserialize(byte[] data)");
                source.Line("throw new NotImplementedException();");
                source.EndScope();

                source.Line();

                source.Method($"public static {packetClassName} Deserialize(MinecraftStream stream)");
                source.Line("throw new NotImplementedException();");
                source.EndScope();
            }
            else if (defaultPacket.UsableInterface == Vocabulary.ClientboundInterface)
            {
                source.Method("public void Serialize(MinecraftStream stream)");
                source.EndScope();

                source.Line();
            }
            else if (defaultPacket.UsableInterface == Vocabulary.ServerboundInterface)
            {
                source.Method("public void Populate(byte[] data)");
                source.EndScope();

                source.Line();

                source.Method("public void Populate(MinecraftStream stream)");
                source.EndScope();

                source.Line();

                source.Method("public ValueTask HandleAsync(Server server, Player player)");
                source.Line("return default;");
                source.EndScope();

                source.Line();

                source.Method($"public static {packetClassName} Deserialize(byte[] data)");
                source.Line("throw new NotImplementedException();");
                source.EndScope();

                source.Line();

                source.Method($"public static {packetClassName} Deserialize(MinecraftStream stream)");
                source.Line("throw new NotImplementedException();");
                source.EndScope();
            }

            source.EndScope();

            context.AddSource($"Common.{packetClassName}.g.cs", source.ToString());

            source.Clear();
        }
    }

    private static readonly JsonSerializerOptions options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, false)
            }
    };

    private static Packet[] GetPackets(string packetsJson) =>
        JsonSerializer.Deserialize<Packet[]>(packetsJson, options)!;

    private sealed class Packet
    {
        public string Name { get; set; } = default!;

        public string ResourceId { get; set; } = default!;

        public string Namespace { get; set; } = default!;

        public string State { get; set; } = default!;

        public int PacketId { get; set; } = default!;

        public string UsableInterface => $"I{Namespace}Packet";
    }
}
