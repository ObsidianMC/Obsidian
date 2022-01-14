namespace Obsidian.Net.Packets;

internal static class ObjectExtensions
{
    internal static string AsString(this object @this)
    {
        var type = @this.GetType();

        return $"{type.Name}{{{string.Join(", ", type.GetProperties().Select(x => $"{x.Name}=\"{x.GetValue(@this)}\""))}}}";
    }
}
