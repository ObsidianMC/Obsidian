using System;

namespace Obsidian.API
{
    public interface IHoverComponent
    {
#nullable disable
        internal static Func<IHoverComponent> createNew;
#nullable restore
        public static IHoverComponent CreateNew() => createNew();

        public EHoverAction Action { get; set; }
        public object Contents { get; set; }
        public string? Translate { get; set; }

        public static IHoverComponent CreateNew(EHoverAction action, object contents)
        {
            var textComponent = CreateNew();
            textComponent.Action = action;
            textComponent.Contents = contents;
            return textComponent;
        }

        public static IHoverComponent CreateNew(EHoverAction action, object contents, string? translate)
        {
            var textComponent = CreateNew();
            textComponent.Action = action;
            textComponent.Contents = contents;
            textComponent.Translate = translate;
            return textComponent;
        }
    }
}
