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
    }
}
