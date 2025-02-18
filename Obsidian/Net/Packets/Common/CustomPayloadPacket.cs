using Obsidian.Entities;
using System.Text;
using static Microsoft.CodeAnalysis.CSharp.SyntaxTokenParser;

namespace Obsidian.Net.Packets.Common;

public partial record class CustomPayloadPacket
{
    private const string Brand = "minecraft:brand";
    private const string Register = "minecraft:register";
    private const string Unregister = "minecraft:unregister";

    public string ResourceLocation { get; set; } = default!;

    public byte[] PayloadData { get; set; } = default!;

    public CustomPayloadPacket() { }

    public CustomPayloadPacket(string channel, byte[] data)
    {
        ResourceLocation = channel;
        PayloadData = data;
    }

    public override void Populate(INetStreamReader reader)
    {
        ResourceLocation = reader.ReadString();
        PayloadData = reader.ReadUInt8Array((int)(reader.Length - reader.Position));
    }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(this.ResourceLocation);
        writer.WriteByteArray(this.PayloadData);
    }

    public override async ValueTask HandleAsync(Server server, Player player)
    {
        await using var stream = new MinecraftStream(this.PayloadData);

        switch (this.ResourceLocation)
        {
            case Brand:
                player.client.Brand = stream.ReadString();
                break;

            case Register:
                {
                    var data = Encoding.UTF8.GetString(this.PayloadData);
                    foreach (var item in data.Split("\0", StringSplitOptions.RemoveEmptyEntries))
                    { }

                    break;
                }

            case Unregister:
                {
                    var data = Encoding.UTF8.GetString(this.PayloadData);
                    foreach (var item in data.Split("\0", StringSplitOptions.RemoveEmptyEntries))
                        server.PayloadHandler.RemovePayload(item);

                    break;
                }

            default: // This can be ignored for now
                await server.PayloadHandler.ExecutePayloadAsync(this.ResourceLocation);
                break;
        }
    }
}
