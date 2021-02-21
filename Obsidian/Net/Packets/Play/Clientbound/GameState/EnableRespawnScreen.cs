﻿namespace Obsidian.Net.Packets.Play.Clientbound.GameState
{
    public class EnableRespawnScreen : ChangeGameState<RespawnReason>
    {
        public override RespawnReason Value { get; set; }

        public EnableRespawnScreen(RespawnReason reason)
        {
            Value = reason;
        }
    }

    public enum RespawnReason
    {
        EnableRespawnScreen,

        ImmediatelyRespawn
    }
}
