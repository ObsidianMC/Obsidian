﻿using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login;

public partial class LoginSuccess : IClientboundPacket
{
    [Field(0)]
    public Guid UUID { get; }

    [Field(1)]
    public string Username { get; }

    [Field(3)]
    public List<SkinProperty> SkinProperties { get; init; } = [];

    public int Id => 0x02;

    public LoginSuccess(Guid uuid, string username)
    {
        UUID = uuid;
        Username = username;
    }
}
