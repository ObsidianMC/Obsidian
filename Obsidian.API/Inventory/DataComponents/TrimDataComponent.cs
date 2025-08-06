using Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
using Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;

namespace Obsidian.API.Inventory.DataComponents;
public sealed record class TrimDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Trim;

    public override string Identifier => "minecraft:trim";

    public TrimMaterialElement Material { get; set; }

    public TrimPatternElement Pattern { get; set; }

    public bool ShowInToolTip { get; set; }

    public override void Read(INetStreamReader reader)
    {
        TrimMaterialElement.Read(reader);
        TrimPatternElement.Read(reader);

        this.ShowInToolTip = reader.ReadBoolean();
    }

    public override void Write(INetStreamWriter writer)
    {
        TrimMaterialElement.Write(this.Material, writer);
        TrimPatternElement.Write(this.Pattern, writer);
        writer.WriteBoolean(this.ShowInToolTip);
    }
}
