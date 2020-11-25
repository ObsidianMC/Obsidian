using System;

namespace Obsidian.API
{
    public interface ITextComponent
    {
        internal static Func<ITextComponent> createNew;
        public static ITextComponent CreateNew() => createNew();

        public ETextAction Action { get; set; }
        public string Value { get; set; }
        public string Translate { get; set; }

        public static ITextComponent CreateNew(ETextAction action, string value)
        {
            var textComponent = CreateNew();
            textComponent.Action = action;
            textComponent.Value = value;
            return textComponent;
        }

        public static ITextComponent CreateNew(ETextAction action, string value, string translate)
        {
            var textComponent = CreateNew();
            textComponent.Action = action;
            textComponent.Value = value;
            textComponent.Translate = translate;
            return textComponent;
        }
    }
}
