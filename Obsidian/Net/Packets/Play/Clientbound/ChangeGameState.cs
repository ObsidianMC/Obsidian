using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public abstract partial class ChangeGameState<T> : ISerializablePacket
    {
        [Field(0), ActualType(typeof(byte))]
        public ChangeGameStateReason Reason { get; }

        [Field(1), ActualType(typeof(float))]
        public abstract T Value { get; set; }

        public int Id => 0x1D;

        public ChangeGameState()
        {
        }

        public ChangeGameState(ChangeGameStateReason reason)
        {
            Reason = reason;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }

    public enum ChangeGameStateReason : byte
    {
        NoRespawnBlockAvailable,

        EndRaining,
        BeginRaining,

        ChangeGamemode,

        WinGame,

        DemoEvent,

        ArrowHitPlayer,

        RainLevelChange,
        ThunderLevelChange,

        PlayerPufferfishStingSound,

        PlayElderGuardianMobAppearance,

        EnableRespawnScreen
    }
}
