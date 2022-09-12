using Obsidian.Entities;

namespace Obsidian.Net.Packets.Login;

public class LoginStart : IServerboundPacket
{
    public string Username { get; set; }

    /// <summary>
    /// Whether or not <see cref="Timestamp"/>, <see cref="PublicKey"/> and <see cref="Signature"/> have values.
    /// </summary>
    public bool HasSigData { get; private set; }

    /// <summary>
    /// When the key data will expire.
    /// </summary>
    public DateTimeOffset? Timestamp { get; private set; }

    /// <summary>
    /// The encoded bytes of the public key the client received from Mojang.
    /// </summary>
    public byte[]? PublicKey { get; private set; }

    /// <summary>
    /// The bytes of the public key signature the client received from Mojang. 
    /// </summary>
    public byte[]? Signature { get; private set; }

    public Guid? PlayerUuid { get; set; }

    public int Id => 0x00;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;

    public static LoginStart Deserialize(byte[] data)
    {
        using var stream = new MinecraftStream(data);
        return Deserialize(stream);
    }

    public static LoginStart Deserialize(MinecraftStream stream)
    {
        var packet = new LoginStart();
        packet.Populate(stream);
        return packet;
    }

    public void Populate(byte[] data)
    {
        using var stream = new MinecraftStream(data);
        Populate(stream);
    }


    public void Populate(MinecraftStream stream)
    {
        this.Username = stream.ReadString();
        this.HasSigData = stream.ReadBoolean();

        if (this.HasSigData)
        {
            this.Timestamp = stream.ReadDateTimeOffset();

            this.PublicKey = stream.ReadUInt8Array();
            this.Signature = stream.ReadUInt8Array();
        }

        if (stream.ReadBoolean())
            this.PlayerUuid = stream.ReadGuid();
    }
}
