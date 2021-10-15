using System;

namespace Obsidian.API.Advancements
{
    public sealed class AdvancementDisplay
    {
        public ChatMessage Title { get; init; }

        public ChatMessage Description { get; init; }

        public Item Icon { get; init; }

        public AdvancementFrameType AdvancementFrameType { get; init; }

        public AdvancementFlags Flags { get; init; }

        public string BackgroundTexture { get; init; }

        public float XCoord { get; init; }

        public float YCoord { get; init; }
    }

    [Flags]
    public enum AdvancementFlags
    {
        HasBackgroundTexture = 1,

        ShowToast,

        Hidden = 4
    }

    public enum AdvancementFrameType : int
    {
        Task,

        Challenge,

        Goal
    }
}
