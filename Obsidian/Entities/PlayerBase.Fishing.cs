namespace Obsidian.Entities;

public partial class PlayerBase
{
    /// <summary>The bobber this player cast and has not reeled in, like vanilla's <c>Player.fishing</c>.</summary>
    internal FishingHook? Fishing { get; set; }
}
