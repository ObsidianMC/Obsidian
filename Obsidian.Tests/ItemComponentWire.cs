#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Net;
using Xunit;

namespace Obsidian.Tests;

public class ItemComponentWire
{
    internal static JsonDocument Fixture()
    {
        using var stream = typeof(NetworkBuffer).Assembly.GetManifestResourceStream("Obsidian.Assets.item-components-1.21.11.json")!;
        return JsonDocument.Parse(stream);
    }

    private static Func<string, int, string?> Resolver(JsonDocument fixture) => (registry, id) =>
        fixture.RootElement.GetProperty("registries").TryGetProperty(registry, out var entries) && id >= 0 && id < entries.GetArrayLength()
            ? entries[id].GetString()
            : registry == "minecraft:item" ? ItemsRegistry.Get(id).UnlocalizedName : null;

    public static IEnumerable<object[]> Stacks()
    {
        using var fixture = Fixture();
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetProperty("name").GetString()!, row.GetProperty("wire").GetString()!, row.GetProperty("untrusted").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Stacks))]
    public void VanillaStacksRoundTrip(string name, string wire, string untrusted)
    {
        Assert.NotEmpty(name);
        foreach (var delimited in new[] { false, true })
        {
            var bytes = Convert.FromHexString(delimited ? untrusted : wire);
            var reader = new NetworkBuffer([.. bytes, 0x5a]);
            var stack = reader.ReadItemStack(delimited);
            Assert.NotNull(stack);
            Assert.Equal(bytes.Length, reader.Offset);
            Assert.Equal(0x5a, reader.ReadByte());
            foreach (var component in stack.Patch)
            {
                if (ComponentBuilder.Create(component.Type) is DataComponent model)
                    Assert.Equal(model.GetType(), component.GetType());
            }

            var writer = new NetworkBuffer();
            writer.WriteItemStack(stack, delimited);
            Assert.Equal(bytes, writer.GetBuffer().AsSpan(0, writer.Offset).ToArray());
        }
    }

    [Fact]
    public void SupportedHashesMatchVanilla()
    {
        using var fixture = Fixture();
        var hasher = new NetworkBuffer { ComponentRegistryName = Resolver(fixture) };
        var supported = new HashSet<DataComponentType>();
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var stack = new NetworkBuffer(Convert.FromHexString(row.GetProperty("wire").GetString()!)).ReadItemStack()!;
            foreach (var component in stack.Patch)
            {
                if (!hasher.TryHashDataComponent(component, out var hash))
                    continue;

                Assert.True(row.GetProperty("hashes").TryGetProperty(((int)component.Type).ToString(), out var expected));
                Assert.True(expected.GetInt32() == hash, $"{row.GetProperty("name")} {component.Type}: expected {expected.GetInt32()}, got {hash}");
                supported.Add(component.Type);
            }
        }

        Assert.Equal(64, supported.Count);
        foreach (var required in new[] { DataComponentType.Damage, DataComponentType.MaxDamage, DataComponentType.RepairCost,
            DataComponentType.CustomName, DataComponentType.ItemName, DataComponentType.Lore, DataComponentType.Enchantments,
            DataComponentType.Unbreakable, DataComponentType.PotionContents, DataComponentType.DyedColor,
            DataComponentType.Container, DataComponentType.BundleContents, DataComponentType.WrittenBookContent })
            Assert.Contains(required, supported);
    }

    [Fact]
    public void MerchantOffersMatchVanilla()
    {
        using var fixture = Fixture();
        foreach (var row in fixture.RootElement.GetProperty("merchantOffers").EnumerateArray())
        {
            var bytes = Convert.FromHexString(row.GetString()!);
            var reader = new NetworkBuffer([.. bytes, 0x5a]);
            var offer = reader.ReadMerchantOffer();
            Assert.Equal(bytes.Length, reader.Offset);
            Assert.Equal(0x5a, reader.ReadByte());

            var writer = new NetworkBuffer();
            writer.WriteMerchantOffer(offer);
            Assert.Equal(bytes, writer.GetBuffer().AsSpan(0, writer.Offset).ToArray());
        }
    }

    [Fact]
    public void ComponentIdsMatchVanillaRegistry()
    {
        using var fixture = Fixture();
        var names = fixture.RootElement.GetProperty("componentTypes").EnumerateArray().ToArray();
        Assert.Equal(names.Length, Enum.GetValues<DataComponentType>().Length);
        for (var id = 0; id < names.Length; id++)
            Assert.Equal(names[id].GetString(), OpaqueDataComponent.GetIdentifier((DataComponentType)id));
    }

    [Fact]
    public void HashedStacksMatchVanillaAndDetectDesync()
    {
        using var fixture = Fixture();
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (!row.TryGetProperty("hashed", out var golden))
                continue;

            var bytes = Convert.FromHexString(golden.GetString()!);
            var reader = new NetworkBuffer([.. bytes, 0x5a]) { ComponentRegistryName = Resolver(fixture) };
            var received = reader.ReadHashedItemStack()!;
            Assert.Equal(bytes.Length, reader.Offset);
            Assert.Equal(0x5a, reader.ReadByte());

            var relay = new NetworkBuffer();
            relay.WriteHashedItemStack(received);
            Assert.Equal(bytes, relay.GetBuffer().AsSpan(0, relay.Offset).ToArray());

            var stack = new NetworkBuffer(Convert.FromHexString(row.GetProperty("wire").GetString()!)).ReadItemStack()!;
            var writer = new NetworkBuffer { ComponentRegistryName = Resolver(fixture) };
            var allSupported = stack.Patch.All(component => writer.TryHashDataComponent(component, out _));
            Assert.Equal(allSupported, received.Compare(stack));

            writer.WriteHashedItemStack(stack);
            writer.Reset();
            var computed = writer.ReadHashedItemStack()!;
            foreach (var (type, hash) in computed.HashedComponents)
                Assert.Equal(received.HashedComponents[type], hash);

            Assert.Equal(received.ComponentsToRemove.Order(), computed.ComponentsToRemove.Order());
            Assert.Equal(received.Count, computed.Count);
            Assert.Equal(received.Holder, computed.Holder);

            stack.Count++;
            Assert.False(received.Compare(stack));
            stack.Count--;
            stack.Remove(DataComponentType.CustomData);
            Assert.False(received.Compare(stack));
        }
    }

    [Fact]
    public void EmptyRequiredListsCopiesAndMalformedValues()
    {
        var empty = new NetworkBuffer();
        empty.WriteItemStack(null);
        empty.WriteHashedItemStack((ItemStack?)null);
        empty.Reset();
        Assert.Null(empty.ReadItemStack());
        Assert.Null(empty.ReadHashedItemStack());

        Assert.Throws<InvalidDataException>(() => new NetworkBuffer([0]).ReadRequiredItemStack());
        Assert.Throws<InvalidDataException>(() => new NetworkBuffer([1, 1, 1, 0, 104]).ReadItemStack());
        Assert.Throws<InvalidDataException>(() => new NetworkBuffer([1, 1, 1, 0, 3, 2, 12, 0]).ReadUntrustedItemStack());

        using var fixture = Fixture();
        var bytes = Convert.FromHexString(fixture.RootElement.GetProperty("cases")[0].GetProperty("wire").GetString()!);
        Assert.ThrowsAny<Exception>(() => new NetworkBuffer(bytes[..^1]).ReadItemStack());

        var stack = new NetworkBuffer(bytes).ReadItemStack()!;
        stack.Remove(DataComponentType.MaxDamage);
        var copy = new ItemStack(stack, stack.Count);
        Assert.Equal(0, copy.MaxDamage);
        Assert.Contains(DataComponentType.MaxDamage, copy.RemoveComponents);

        var list = new NetworkBuffer();
        list.WriteItemStackList(null, copy);
        list.Reset();
        var values = list.ReadItemStackList();
        Assert.Equal(2, values.Length);
        Assert.Null(values[0]);
        Assert.Equal(0, values[1]!.MaxDamage);

        copy[DataComponentType.MaxDamage] = ComponentBuilder.MaxDamage with { Value = 999 };
        Assert.DoesNotContain(DataComponentType.MaxDamage, copy.RemoveComponents);
        Assert.Equal(999, copy.MaxDamage);
        copy.Remove(DataComponentType.MaxDamage);
        Assert.True(copy.Add(ComponentBuilder.MaxDamage with { Value = 500 }));
        Assert.DoesNotContain(DataComponentType.MaxDamage, copy.RemoveComponents);
        Assert.Equal(500, copy.MaxDamage);

        var book = new ItemStack(ItemsRegistry.Get(Material.EnchantedBook));
        Assert.True(book.HasEnchantmentGlint);
        book.Remove(DataComponentType.EnchantmentGlintOverride);
        Assert.False(book.HasEnchantmentGlint);
    }

    [Fact]
    public void UnicodeAndMixedChatListsRemainDisplayable()
    {
        using var fixture = Fixture();
        var row = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("name").GetString() == "chat_unicode");
        var stack = new NetworkBuffer(Convert.FromHexString(row.GetProperty("wire").GetString()!)).ReadItemStack()!;
        Assert.Equal("NUL\0 \ud83d\ude00 music \ud834\udd1e", stack.CustomName!.Text);
        Assert.Equal("key.test", stack.Lore[0].Translate);
        Assert.Equal("nested", stack.Lore[0].GetExtraChatComponents().First().Text);
    }

    [Fact]
    public void EmptyKeysAndLongStringsRoundTrip()
    {
        // Empty keys and unsigned string lengths are legal Java NBT, so custom data with them must be written back as read.
        foreach (var length in new[] { 1, 40000 })
        {
            var payload = new NetworkBuffer();
            payload.WriteByte((byte)10);
            payload.WriteByte((byte)8);
            payload.WriteUnsignedShort(0);
            payload.WriteUnsignedShort((ushort)length);
            payload.Write(System.Text.Encoding.ASCII.GetBytes(new string('x', length)));
            payload.WriteByte((byte)0);

            var expected = payload.GetBuffer().AsSpan(0, payload.Offset).ToArray();
            payload.Reset();
            var component = payload.ReadDataComponent(DataComponentType.CustomData);

            var writer = new NetworkBuffer();
            writer.WriteDataComponent(component);
            Assert.Equal(expected, writer.GetBuffer().AsSpan(0, writer.Offset).ToArray());
        }
    }

    [Fact]
    public void DisplayDataIsTypedAndEditsAreWritten()
    {
        using var fixture = Fixture();
        var row = fixture.RootElement.GetProperty("cases")[0];
        var stack = new NetworkBuffer(Convert.FromHexString(row.GetProperty("wire").GetString()!)).ReadItemStack()!;
        Assert.Equal(17, stack.Damage);
        Assert.Equal(1700, stack.MaxDamage);
        Assert.NotNull(stack.CustomName);
        Assert.Equal(2, stack.Lore.Count);
        Assert.Equal(2, stack.Enchantments.Count);
        Assert.True(stack.HasEnchantmentGlint);

        stack.GetComponent<SimpleDataComponent<int>>(DataComponentType.Damage)!.Value = 42;
        var writer = new NetworkBuffer();
        writer.WriteItemStack(stack);
        writer.Reset();
        Assert.Equal(42, writer.ReadItemStack()!.Damage);
    }
}
