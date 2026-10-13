using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Nbt;
using Obsidian.Net;
using System;
using Xunit;

namespace Obsidian.Tests;

public sealed class ItemComponentValues
{
    [Fact]
    public void ChatAndCollectionsCompareByValueAfterCopyAndDecode()
    {
        var name = ComponentBuilder.CustomName with
        {
            Value = ChatMessage.Simple("Hello").AddExtra("extra").AddChatComponent("argument")
        };
        var lore = ComponentBuilder.Lore with { Value = [name.Value!] };
        var pages = ComponentBuilder.WritableBookContent with
        {
            Value = [new Page { RawContent = "First" }, new Page { RawContent = "Second" }]
        };
        DataComponent[] components = [name, lore, pages];
        foreach (var component in components)
        {
            // Warm the key before decoding a record copy, exercising key invalidation on ReadCore.
            var originalHash = component.GetHashCode();
            using var writer = new NetworkBuffer();
            component.Write(writer);
            using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
            var decoded = component.Read(reader);
            Assert.Equal(component, decoded);
            Assert.Equal(originalHash, decoded.GetHashCode());
        }

        Assert.Equal(name, name with { Value = name.Value });
        Assert.Equal(lore, lore with { Value = lore.Value });
        Assert.NotEqual(name, name with { Value = ChatMessage.Simple("Hello").AddExtra("different") });
        Assert.NotEqual(pages, pages with { Value = [new Page { RawContent = "Changed" }] });
    }

    [Fact]
    public void DecodeCopiesReaderOwnedMutableValues()
    {
        var owned = ChatMessage.Simple("Original").AddExtra("extra");
        var component = new SimpleDataComponent<ChatMessage>(
            DataComponentType.CustomName,
            "minecraft:custom_name",
            (writer, value) => writer.WriteChat(value),
            _ => owned,
            value => value is null ? null! : ChatMessage.Simple(value.Text!).AddExtra(value.Extra![0].Text!));
        using var reader = new NetworkBuffer();
        var decoded = (SimpleDataComponent<ChatMessage>)component.Read(reader);
        owned.Text = "Changed";
        owned.Extra![0].Text = "Changed extra";

        Assert.Equal("Original", decoded.Value!.Text);
        Assert.Equal("extra", decoded.Value.Extra![0].Text);
    }

    [Fact]
    public void NestedStacksAndSavedNbtDoNotShareMutableTags()
    {
        var saved = new NbtCompound
        {
            new NbtTag<string>("id", "minecraft:stone"),
            new NbtTag<int>("count", 2),
            new NbtCompound("components")
            {
                new NbtCompound("review:unknown") { new NbtTag<int>("value", 7) }
            }
        };
        var original = saved.ItemFromNbt()!;
        var component = ComponentBuilder.Container with { Value = [original] };
        var exposed = component.Value[0];
        var exported = exposed.ToNbt();
        Unknown(exported).Add(new NbtTag<int>("added", 1));
        Unknown(saved).Add(new NbtTag<int>("input", 1));
        exposed.Count = 9;

        var retained = component.Value[0];
        var retainedTag = Unknown(retained.ToNbt());
        Assert.Equal(2, retained.Count);
        Assert.Equal(7, retainedTag.GetInt("value"));
        Assert.False(retainedTag.HasTag("added"));
        Assert.False(retainedTag.HasTag("input"));
        Assert.Equal(original, retained);
    }

    private static NbtCompound Unknown(NbtCompound stack) =>
        Assert.IsType<NbtCompound>(Assert.IsType<NbtCompound>(stack["components"])["review:unknown"]);
}
