using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

/// <summary>
/// Replays <c>mining_progress.json</c>, vanilla's destroy progress for a player (recorded by
/// <c>MiningFixtures.java</c>): Obsidian must compute the same progress, and break the block as the player starts
/// mining exactly when it's 1 or more, since their client breaks those blocks at once and never says it finished.
/// </summary>
public sealed class VanillaMining(ITestOutputHelper output)
{
    private static readonly Vector target = new(4, 70, 4);

    [Fact]
    public async Task ReplayDestroyProgressReference()
    {
        using var stream = typeof(VanillaMining).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.mining_progress.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());

        await using var context = TestServerFactory.CreateServer($"mining-{Guid.NewGuid():N}");
        var chunk = WorldBlockEntities.AddEmptyChunk(context.World);
        var player = await WorldBlockEntities.JoinAsync(context);
        player.GameMode = GameMode.Survival;

        await FixtureCases.AssertAllAsync(
            cases,
            probe => probe.GetProperty("name").GetString()!,
            probe => Replay(probe, context, chunk, player));
        output.WriteLine($"Replayed {cases.Length} mining cases.");
    }

    // Vanilla's client only knows Efficiency's mining bonus from the server's attribute sync: without it, the
    // client mines slower than the server, which breaks blocks the client still shows as being mined.
    [Fact]
    public async Task EnchantmentModifiersReachTheClient()
    {
        await using var context = TestServerFactory.CreateServer($"mining-sync-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var player = await WorldBlockEntities.JoinAsync(context);
        var client = (TestClient)player.Client;

        // Holding an Efficiency V pickaxe.
        var pickaxe = new ItemStack(ItemsRegistry.DiamondPickaxe);
        var efficiency = EnchantmentsRegistry.All.Single(entry => entry.Identifier == "minecraft:efficiency");
        EnchantmentHelper.Enchant(pickaxe, efficiency, 5);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, pickaxe);
        player.UpdateEquipmentAttributes();
        client.ClearPackets();
        player.SendAttributeChanges();

        var sent = MiningEfficiency(client);
        var modifier = Assert.Single(sent.Modifiers);
        Assert.Equal("minecraft:enchantment.efficiency/mainhand", modifier.Id);
        Assert.Equal(26, modifier.Amount);

        // Putting it away takes the bonus away again.
        player.Inventory.SetItem(player.CurrentHeldItemSlot, null);
        player.UpdateEquipmentAttributes();
        client.ClearPackets();
        player.SendAttributeChanges();

        Assert.Empty(MiningEfficiency(client).Modifiers);
    }

    private static AttributeSnapshot MiningEfficiency(TestClient client) => client.SentPackets
        .OfType<UpdateAttributesPacket>()
        .SelectMany(packet => packet.Attributes)
        .Single(attribute => attribute.ProtocolId == AttributeIds.MiningEfficiency);

    private static async Task Replay(JsonElement probe, RealServerContext context, Chunk chunk, Obsidian.Entities.Player player)
    {
        var block = BlocksRegistry.Get(probe.GetProperty("block").GetString()!);
        var expected = Progress(probe.GetProperty("progress"));

        // The player, as the case describes them.
        Equip(player, probe);
        player.ClearPotionEffects();
        if (probe.TryGetProperty("effects", out var effects))
        {
            foreach (var effect in effects.EnumerateObject())
                player.AddPotionEffect(EffectId(effect.Name), 200, effect.Value.GetInt32());
        }

        var onGround = !probe.TryGetProperty("onGround", out var grounded) || grounded.GetBoolean();
        player.MovementFlags = onGround ? MovementFlags.OnGround : MovementFlags.None;

        var eyeInWater = probe.TryGetProperty("eyeInWater", out var water) && water.GetBoolean();
        var eye = (Vector)PlayerBase.EyePosition(player).Floor();
        chunk.SetBlock(eye.X, eye.Y, eye.Z, eyeInWater ? BlocksRegistry.Water : BlocksRegistry.Air);

        // The progress itself.
        Assert.Equal(expected, player.DestroyProgress(block));

        // Starting to mine breaks the block at once exactly when one tick of mining breaks it all.
        chunk.SetBlock(target.X, target.Y - 1, target.Z, BlocksRegistry.Stone);
        chunk.SetBlock(target.X, target.Y, target.Z, block);
        await StartDigging().HandleAsync(context.Server, player);

        var broken = chunk.GetBlock(target.X, target.Y, target.Z).RegistryId != block.RegistryId;
        Assert.Equal(expected >= 1, broken);
    }

    private static void Equip(Obsidian.Entities.Player player, JsonElement probe)
    {
        var held = probe.TryGetProperty("item", out var item) ? Stack(item) : null;
        var helmet = probe.TryGetProperty("helmet", out var worn) ? Stack(worn) : null;

        player.Inventory.SetItem(player.CurrentHeldItemSlot, held);
        player.Inventory.SetItem(5, helmet);

        // As the player's tick would: their equipment's modifiers, Efficiency's and Aqua Affinity's among them.
        player.UpdateEquipmentAttributes();
    }

    // An item as the fixture names it: {"id": ..., "enchantments": {"minecraft:efficiency": 5}}.
    private static ItemStack Stack(JsonElement json)
    {
        var stack = new ItemStack(ItemsRegistry.Get(json.GetProperty("id").GetString()!));
        if (!json.TryGetProperty("enchantments", out var enchantments))
            return stack;

        foreach (var enchantment in enchantments.EnumerateObject())
        {
            var definition = EnchantmentsRegistry.All.Single(entry => entry.Identifier == enchantment.Name);
            EnchantmentHelper.Enchant(stack, definition, enchantment.Value.GetInt32());
        }

        return stack;
    }

    // An effect's id as the entity effect API numbers it, from its registry name (minecraft:mining_fatigue).
    private static int EffectId(string name)
    {
        var words = name["minecraft:".Length..].Split('_');
        var pascal = string.Concat(words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

        return (int)Enum.Parse<PotionEffect>(pascal) - 1;
    }

    // The progress vanilla recorded: a number, or a string for infinity, which JSON numbers can't hold.
    private static float Progress(JsonElement json) => json.ValueKind == JsonValueKind.String
        ? float.Parse(json.GetString()!, CultureInfo.InvariantCulture)
        : json.GetSingle();

    private static PlayerActionPacket StartDigging()
    {
        using var writer = new NetworkBuffer();
        writer.WriteVarInt((int)PlayerActionStatus.StartedDigging);
        writer.WritePosition(target);
        writer.WriteByte((byte)BlockFace.Up);
        writer.WriteVarInt(1);

        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
        var packet = new PlayerActionPacket();
        packet.Populate(reader);

        return packet;
    }
}
