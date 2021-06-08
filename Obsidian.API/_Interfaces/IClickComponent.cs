using System;

namespace Obsidian.API
{
    public interface IClickComponent
    {
        internal static Func<IClickComponent> createNew;
        public static IClickComponent CreateNew() => createNew();

        public EClickAction Action { get; set; }
        public string Value { get; set; }
        public string? Translate { get; set; }

        public static IClickComponent CreateNew(EClickAction action, string value)
        {
            var textComponent = CreateNew();
            textComponent.Action = action;
            textComponent.Value = value;
            return textComponent;
        }

        public static IClickComponent CreateNew(EClickAction action, string value, string? translate)
        {
            var textComponent = CreateNew();
            textComponent.Action = action;
            textComponent.Value = value;
            textComponent.Translate = translate;
            return textComponent;
        }
    }
}
