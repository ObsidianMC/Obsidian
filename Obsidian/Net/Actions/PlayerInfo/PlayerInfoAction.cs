namespace Obsidian.Net.Actions.PlayerInfo;
[Flags]
public enum PlayerInfoAction : sbyte
{
    AddPlayer = 0x01,
    InitChat = 0x02,
    UpdateGamemode = 0x04,
    UpdateListed = 0x08,
    UpdateLatency = 0x10,
    UpdateDisplayName = 0x20
}
