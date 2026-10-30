using Obsidian.API;
using Obsidian.API.Advancements;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Entities;
using Obsidian.Net;
using Obsidian.Net.ChatMessageTypes;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class PlayPayloads
{
    [Theory]
    [InlineData("set_player_team")]
    [InlineData("level_particles")]
    [InlineData("explode")]
    [InlineData("update_advancements")]
    [InlineData("game_rule_values")]
    [InlineData("set_game_rule")]
    [InlineData("low_disk_space_warning")]
    [InlineData("add_transient_block")]
    [InlineData("update_attributes")]
    [InlineData("player_chat")]
    [InlineData("delete_chat")]
    [InlineData("chat")]
    [InlineData("chat_command_signed")]
    [InlineData("pick_item_from_block")]
    [InlineData("damage_event")]
    [InlineData("disguised_chat")]
    [InlineData("update_recipes")]
    [InlineData("recipe_book_add")]
    public async Task PayloadsMatchVanilla(string packet)
    {
        using var fixture = ReadFixture();
        var root = fixture.RootElement;
        var references = root.GetProperty(packet);
        switch (packet)
        {
            case "set_player_team":
                var modes = new[] { TeamModeOption.CreateTeam, TeamModeOption.UpdateTeam, TeamModeOption.RemoveTeam,
                    TeamModeOption.AddEntities, TeamModeOption.RemoveEntities, TeamModeOption.UpdateTeam };
                for (var i = 0; i < modes.Length; i++)
                {
                    Equal(references[i], new SetPlayerTeamPacket
                    {
                        TeamName = "fixture", Mode = modes[i], TeamDisplayName = "Team", TeamPrefix = "[", TeamSuffix = "]",
                        FriendlyFlags = TeamFriendlyFlags.AllowFriendlyFire | TeamFriendlyFlags.CanSeeInvisibleTeammates,
                        NameTagVisibility = i == 5 ? NameTagVisibility.Never : NameTagVisibility.HideForOtherTeams,
                        CollisionRule = i == 5 ? CollisionRule.PushOwnTeam : CollisionRule.Never,
                        TeamColor = i == 5 ? TeamColor.Reset : TeamColor.Red,
                        Entities = [i == 3 ? "Steve" : "Alex"]
                    });
                }

                break;

            case "level_particles":
                foreach (var reference in references.EnumerateArray())
                {
                    for (var i = 0; i < 3; i++)
                    {
                        Equal(reference.GetProperty("wire")[i], new LevelParticlesPacket
                        {
                            Data = new SimpleParticle((ParticleType)reference.GetProperty("id").GetInt32()),
                            OverrideLimiter = true, Position = new(-12.25, 64.5, 31.875), Offset = new(.25f, .5f, .75f),
                            Speed = new(1, 2, 3), ParticleCount = 301, Randomization = (ParticleRandomization)i
                        });
                    }
                }

                // The captures always set per-axis speeds and leave AlwaysShow off. Without Speed, MaxSpeed fills every
                // axis, and AlwaysShow is the flag after OverrideLimiter.
                var ink = new SimpleParticle(ParticleType.SquidInk);
                using (var fallback = new NetworkBuffer())
                using (var perAxis = new NetworkBuffer())
                {
                    new LevelParticlesPacket { Data = ink, AlwaysShow = true, Position = new(1, 62, 3), MaxSpeed = .5f, ParticleCount = 30 }
                        .Serialize(fallback);
                    new LevelParticlesPacket { Data = ink, AlwaysShow = true, Position = new(1, 62, 3), Speed = new(.5f), ParticleCount = 30 }
                        .Serialize(perAxis);
                    var wire = fallback.AsSpan(0, fallback.Size).ToArray();
                    Assert.Equal(perAxis.AsSpan(0, perAxis.Size).ToArray(), wire);
                    using var flags = new NetworkBuffer(wire);
                    Assert.Equal((int)ParticleType.SquidInk, flags.ReadVarInt());
                    Assert.False(flags.ReadBoolean());
                    Assert.True(flags.ReadBoolean());
                }
                break;

            case "explode":
                for (var i = 0; i < 2; i++)
                {
                    Equal(references[i], new ExplodePacket
                    {
                        Center = new(-12.25, 64.5, 31.875), Radius = 4, BlockCount = i == 0 ? 301 : 0,
                        PlayerKnockback = i == 0 ? new Velocity(.25, -.5, 1.5) : null,
                        ExplosionParticle = new SimpleParticle(ParticleType.ExplosionEmitter),
                        ExplosionSound = new SoundEffect
                        {
                            SoundId = "fixture:boom", FixedRange = 24, SoundCategory = SoundCategory.Blocks,
                            Volume = 1, Pitch = 1, Seed = 0
                        },
                        ExplosionParticleInfo = i == 0 ? [new Weighted<ExplosionRecord>
                        {
                            Value = new() { Particle = new SimpleParticle(ParticleType.Smoke), Scaling = .5f, Speed = .75f }, Weight = 3
                        }] : [],
                        PlaySound = i == 0
                    });
                }

                break;

            case "update_advancements":
                CheckAdvancements(references);
                break;

            case "game_rule_values":
                CheckGameRules(root, references);
                break;

            case "set_game_rule":
                var reader = new NetworkBuffer(Convert.FromHexString(references[0].GetString()!));
                var edit = new SetGameRulePacket();
                edit.Populate(reader);
                Assert.Equal(reader.Size, reader.Offset);
                Assert.Equal(new[] { KeyValuePair.Create("minecraft:keep_inventory", "true"),
                    KeyValuePair.Create("minecraft:random_tick_speed", "17") }, edit.Entries);
                await CheckGameRuleEditingAsync(edit);
                break;

            case "low_disk_space_warning":
                Equal(references[0], new LowDiskSpaceWarningPacket());
                break;

            case "add_transient_block":
                Equal(references[0], new AddTransientBlockPacket(new(-12, 70, 23), BlocksRegistry.Stone.DefaultId));
                break;

            case "update_attributes":
                CheckAttributes(root, references);
                break;

            case "delete_chat":
                Equal(references[0], new DeleteChatPacket { Signature = Signature });
                Equal(references[1], new DeleteChatPacket { Signature = default, SignatureIndex = 301 });
                break;

            case "player_chat":
                for (var i = 0; i < 3; i++)
                {
                    Equal(references[i], new PlayerChatPacket
                    {
                        GlobalIndex = 7, Sender = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"), Index = 301,
                        MessageSignature = i == 0 ? default : Signature, UnsignedContent = i == 0 ? null : ChatMessage.Simple("Unsigned"),
                        Body = new PlayerChatMessageBody
                        {
                            Content = "Hello", Signature = new() { Timestamp = Timestamp, Salt = -918273645, Value = default },
                            PackedLastSeen = [new(-1, Signature), new(301, default)]
                        },
                        FilterType = (ChatFilterType)i, FilterMask = [unchecked((long)0x8000000000000002), 64],
                        ChatFormatting = new() { ChatType = 0, SenderName = "Alex", TargetName = i == 0 ? null : ChatMessage.Simple("Steve") }
                    });
                }

                break;

            case "chat":
                var chat = new ChatPacket();
                for (var i = 0; i < 2; i++)
                {
                    var input = new NetworkBuffer(Convert.FromHexString(references[i].GetString()!));
                    chat.Populate(input);
                    Assert.Equal(input.Size, input.Offset);
                    Assert.Equal("Hello", chat.Message);
                    Assert.Equal(Timestamp, chat.Timestamp);
                    Assert.Equal(-918273645, chat.Salt);
                    Assert.Equal(i == 0 ? Signature : [], chat.Signature.ToArray());
                    Assert.Equal(new ChatAcknowledgement(301, 0x80009, 77), chat.Acknowledgement);
                }
                break;

            case "chat_command_signed":
                var signedInput = new NetworkBuffer(Convert.FromHexString(references[0].GetString()!));
                var signed = new ChatCommandSignedPacket();
                signed.Populate(signedInput);
                Assert.Equal(signedInput.Size, signedInput.Offset);
                Assert.Equal("say Hello", signed.Command);
                Assert.Equal(Timestamp, signed.Timestamp);
                Assert.Equal(-918273645, signed.Salt);
                Assert.Equal("message", Assert.Single(signed.ArgumentSignatures).ArgumentName);
                Assert.Equal(Signature, signed.ArgumentSignatures[0].Signature.ToArray());
                Assert.Equal(new ChatAcknowledgement(301, 0x80009, 77), signed.Acknowledgement);
                break;

            case "pick_item_from_block":
                var pickInput = new NetworkBuffer(Convert.FromHexString(references[0].GetString()!));
                var pick = new PickItemFromBlockPacket();
                pick.Populate(pickInput);
                Assert.Equal(pickInput.Size, pickInput.Offset);
                Assert.Equal(new Vector(-12, 70, 23), pick.Position);
                Assert.True(pick.IncludeData);
                break;

            case "damage_event":
                Equal(references[0], new DamageEventPacket(301, "minecraft:arrow", 2, 3, new VectorD(-12.25, 64.5, 31.875)));
                Equal(references[1], new DamageEventPacket(301, "minecraft:arrow", null, null, null));
                break;

            case "disguised_chat":
                for (var i = 0; i < 2; i++)
                {
                    Equal(references[i], new DisguisedChatPacket
                    {
                        Message = "Hello", ChatType = 0, SenderName = "Alex",
                        TargetName = i == 0 ? null : ChatMessage.Simple("Steve")
                    });
                }

                break;

            case "update_recipes":
                var recipes = new Dictionary<string, IRecipe>();
                foreach (var type in new[] { CraftingType.Smelting, CraftingType.Blasting })
                {
                    recipes.Add(type.ToString(), new SmeltingRecipe
                    {
                        Identifier = type.ToString(), Type = type, Experience = 0.7f, CookingTime = 200,
                        Category = CookingBookCategory.Misc, Ingredient = [new ItemStack(ItemsRegistry.RawIron)],
                        Result = [new ItemStack(ItemsRegistry.IronIngot)]
                    });
                }

                recipes.Add("cut", new CuttingRecipe
                {
                    Identifier = "cut", Type = CraftingType.Stonecutting, Ingredient = [new ItemStack(ItemsRegistry.Stone)],
                    Result = [new ItemStack(ItemsRegistry.StoneSlab, 2)], Count = 2
                });
                Equal(references[0], new UpdateRecipesPacket(recipes));
                break;

            case "recipe_book_add":
                Ingredient stone = [new ItemStack(ItemsRegistry.Stone)];
                Ingredient either = [new ItemStack(ItemsRegistry.Cobblestone), new ItemStack(ItemsRegistry.Andesite)];
                var shaped = new ShapedRecipe
                {
                    Identifier = "shaped", Type = CraftingType.CraftingShaped, Category = CraftingBookCategory.Building,
                    Result = [new ItemStack(ItemsRegistry.StoneBricks, 4)], Pattern = ["S ", "ES"],
                    Key = new Dictionary<char, Ingredient> { ['S'] = stone, ['E'] = either }
                };
                var shapeless = new ShapelessRecipe
                {
                    Identifier = "shapeless", Type = CraftingType.CraftingShapeless, Category = CraftingBookCategory.Misc,
                    Result = [new ItemStack(ItemsRegistry.Gravel)], Ingredients = [either]
                };
                Equal(references[0], new RecipeBookAddPacket([new(5, shaped, 3), new(6, shapeless, null)], replace: true));
                break;
        }
    }

    internal static JsonDocument ReadFixture() => JsonDocument.Parse(typeof(Server).Assembly
        .GetManifestResourceStream("Obsidian.Assets.remaining-play-26.3.json")!);

    private static readonly byte[] Signature = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1767000123456);

    // Pairs each value with its bits, so a failure still shows the values while -0 and +0 stay different.
    private static void AssertSameFloat(float expected, float actual) => Assert.Equal(
        (expected, BitConverter.SingleToInt32Bits(expected)), (actual, BitConverter.SingleToInt32Bits(actual)));

    private static Advancement Definition(JsonElement row)
    {
        var id = row.GetProperty("id").GetString()!;
        var parent = row.GetProperty("parent").GetString()!;
        var flags = AdvancementFlags.ShowToast | (parent.Length == 0 ? AdvancementFlags.HasBackgroundTexture : 0);
        var display = row.GetProperty("display").GetBoolean()
            ? new AdvancementDisplay("Title", "Description", ItemsRegistry.Diamond, AdvancementFrameType.Goal,
                flags, "minecraft:gui/advancements/backgrounds/stone", 0, 0) { IconStack = new(ItemsRegistry.Diamond, 3) }
            : null;
        return new(id, parent, display, null, [new Criteria("done", true)]);
    }

    private static async Task CheckGameRuleEditingAsync(SetGameRulePacket edit)
    {
        await using var context = TestServerFactory.CreateServer($"packet-rules-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Editor");
        context.Server.OnlinePlayers.TryAdd(player.Uuid, player);
        var client = (TestClient)player.Client;
        await edit.HandleAsync(context.Server, player);
        Assert.Empty(context.World.LevelData.GameRules);
        SetPermissionLevel(context.Server, player, 1);
        await edit.HandleAsync(context.Server, player);
        Assert.Empty(context.World.LevelData.GameRules);
        SetPermissionLevel(context.Server, player, 2);
        await edit.HandleAsync(context.Server, player);
        Assert.True(context.World.LevelData.GetBooleanRule("keep_inventory", false));
        Assert.Equal(17, context.World.LevelData.GetIntegerRule("random_tick_speed"));
        Assert.Equal(2, client.SentPackets.OfType<GameRuleValuesPacket>().Count());
        Assert.False(await GameRuleSynchronization.SetAsync(context.Server, "random_tick_speed", "-1"));
        Assert.Equal(17, context.World.LevelData.GetIntegerRule("random_tick_speed"));
        Assert.True(await GameRuleSynchronization.SetAsync(context.Server, "minecraft:random_tick_speed", "3"));
        Assert.Equal("3", client.SentPackets.OfType<GameRuleValuesPacket>().Last().Values["minecraft:random_tick_speed"]);
    }

    // Fill the operator list in memory, without writing ops.json in the developer's configuration directory.
    private static void SetPermissionLevel(Server server, Obsidian.Entities.Player player, int level)
    {
        var entries = (IList)typeof(OperatorList).GetField("operators", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(server.Operators)!;
        entries.Clear();
        var type = entries.GetType().GetGenericArguments()[0];
        var entry = Activator.CreateInstance(type)!;
        type.GetProperty("Uuid")!.SetValue(entry, player.Uuid);
        type.GetProperty("Username")!.SetValue(entry, player.Username);
        type.GetProperty("Level")!.SetValue(entry, level);
        entries.Add(entry);
    }

    private static void CheckAdvancements(JsonElement references)
    {
        foreach (var reference in references.EnumerateArray())
        {
            var definitions = reference.GetProperty("definitions").EnumerateArray().Select(Definition).ToArray();
            var placed = AdvancementsRegistry.PlaceDisplays(definitions);
            foreach (var expected in reference.GetProperty("definitions").EnumerateArray())
            {
                var display = placed[expected.GetProperty("id").GetString()!].Display;

                // Positions are sent as floats, so compare their bits: a one-ulp difference is a parity bug.
                AssertSameFloat(expected.GetProperty("x").GetSingle(), display?.XCoord ?? 0);
                AssertSameFloat(expected.GetProperty("y").GetSingle(), display?.YCoord ?? 0);
            }
            Equal(reference.GetProperty("wire"), new UpdateAdvancementsPacket
            {
                Reset = true, Added = definitions.Select(definition => placed[definition.Identifier]).ToList(),
                Removed = ["fixture:removed"]
            });
        }
    }

    private static void CheckGameRules(JsonElement root, JsonElement references)
    {
        var values = GameRuleSynchronization.Snapshot(new LevelData());
        Assert.Equal(59, values.Values.Count);
        Equal(references[0], values);

        foreach (var row in root.GetProperty("game_rule_parsing").EnumerateArray())
        {
            Assert.Equal(row[2].GetBoolean(), Accepts(row[0].GetString()!, row[1].GetString()!));
        }

        // Each integer rule takes its bounds, and nothing past them.
        foreach (var bounds in root.GetProperty("game_rule_bounds").EnumerateObject())
        {
            long min = bounds.Value[0].GetInt32();
            long max = bounds.Value[1].GetInt32();
            Assert.True(Accepts(bounds.Name, min));
            Assert.True(Accepts(bounds.Name, max));
            Assert.False(Accepts(bounds.Name, min - 1));
            Assert.False(Accepts(bounds.Name, max + 1));
        }

        Assert.False(Accepts("fixture:keep_inventory", "true"));
        Assert.False(Accepts("minecraft:unknown", "true"));

        static bool Accepts(string rule, object value) => GameRuleSynchronization.TryNormalize(
            rule,
            Convert.ToString(value, CultureInfo.InvariantCulture)!,
            out _,
            out _);
    }

    private static void CheckAttributes(JsonElement root, JsonElement references)
    {
        var attributes = new List<AttributeSnapshot>();
        var modifiers = ImmutableArray.Create(new EntityAttributeModifier("fixture:add", 1.5, AttributeOperation.Add),
            new EntityAttributeModifier("fixture:base", -.25, AttributeOperation.MulBase),
            new EntityAttributeModifier("fixture:total", .75, AttributeOperation.MulTotal));
        foreach (var row in root.GetProperty("attribute_ranges").EnumerateArray())
        {
            var name = row[0].GetString()!;
            Assert.True(VanillaAttributes.TryGetProtocolId(name, out var id));
            Assert.True(VanillaAttributes.TryGetRange(name, out var range));
            Assert.Equal(row[1].GetDouble(), range.Min);
            Assert.Equal(row[2].GetDouble(), range.Max);
            Assert.Equal(row[3].GetBoolean(), range.Syncable);
            attributes.Add(new(id, row[4].GetDouble(), modifiers));
        }
        Equal(references[0], new UpdateAttributesPacket(301, attributes));
    }

    private static void Equal(JsonElement expected, IClientboundPacket packet)
    {
        using var buffer = new NetworkBuffer();
        packet.Serialize(buffer);
        Assert.Equal(Convert.FromHexString(expected.GetString()!), buffer.AsSpan(0, buffer.Size).ToArray());
    }

    private sealed class SimpleParticle(ParticleType type) : ParticleData
    {
        public override ParticleType ParticleType => type;
    }
}
