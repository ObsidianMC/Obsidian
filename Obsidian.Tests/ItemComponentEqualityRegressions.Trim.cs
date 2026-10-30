using Obsidian.API;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registry.Codecs.ArmorTrims;
using Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
using Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;
using Xunit;

namespace Obsidian.Tests;

public sealed partial class ItemComponentEqualityRegressions
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IndependentTrimDescriptionsHaveEqualHashesAndMerge(bool material)
    {
        var first = DescribedTrim(material, TrimChat("same"));
        var second = DescribedTrim(material, TrimChat("same"));

        AssertMerge(first, second);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void TrimDescriptionChildrenRemainSignificant(bool material, bool extra)
    {
        var firstChat = TrimChat("same");
        var secondChat = TrimChat("same");

        if (extra)
        {
            secondChat.Extra![0].Text = "changed";
        }
        else
        {
            secondChat.With![0].Text = "changed";
        }

        var first = DescribedTrim(material, firstChat);
        var second = DescribedTrim(material, secondChat);

        AssertDoNotMerge(first, second);
    }

    [Theory]
    [InlineData("material-id")]
    [InlineData("pattern-id")]
    [InlineData("palette")]
    [InlineData("asset")]
    [InlineData("decal")]
    [InlineData("tooltip")]
    public void TrimOtherFieldsRemainSignificant(string field)
    {
        var first = DescribedTrim(true, TrimChat("same"));
        var second = field switch
        {
            "material-id" => first with { MaterialRegistryId = 1 },
            "pattern-id" => first with { PatternRegistryId = 1 },
            "palette" => first with { Material = first.Material with { PaletteId = "test:changed" } },
            "asset" => first with { Pattern = first.Pattern with { AssetId = "test:changed" } },
            "decal" => first with { Pattern = first.Pattern with { Decal = true } },
            "tooltip" => first with { ShowInToolTip = true },
            _ => throw new System.ArgumentOutOfRangeException(nameof(field))
        };

        AssertDoNotMerge(first, second);
    }

    private static ChatMessage TrimChat(string text) => new ChatMessage { Translate = "test:description" }
        .AddExtra(ChatMessage.Simple(text))
        .AddChatComponent(ChatMessage.Simple("argument"));

    private static TrimDataComponent DescribedTrim(bool material, ChatMessage description) => new()
    {
        Material = new TrimMaterialElement
        {
            PaletteId = "test:palette",
            Description = new TrimDescription { Translate = "test:material" }
        },
        Pattern = new TrimPatternElement
        {
            AssetId = "test:pattern",
            Description = new TrimDescription { Translate = "test:pattern" },
            Decal = false
        },
        MaterialDescription = material ? description : null,
        PatternDescription = material ? null : description
    };
}
