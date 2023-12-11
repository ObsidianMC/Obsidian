﻿using Obsidian.API.Boss;

namespace Obsidian.Net.Actions.BossBar;

public sealed class BossBarUpdateFlagsAction : BossBarAction
{
    public BossBarFlags Flags { get; set; }

    public BossBarUpdateFlagsAction() : base(5) { }

    public override void WriteTo(MinecraftStream stream)
    {
        base.WriteTo(stream);

        stream.WriteUnsignedByte((byte)Flags);
    }

    public override async Task WriteToAsync(MinecraftStream stream)
    {
        await base.WriteToAsync(stream);

        await stream.WriteUnsignedByteAsync((byte)Flags);
    }
}
