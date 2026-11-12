using System;
using System.Text.Json;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Effects;
using Obsidian.Entities;
using Obsidian.Net;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

public class ArmorStandInvisibility
{
    private const int InvisibilityId = (int)PotionEffect.Invisibility - 1;

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ReplayVanillaInvisibilityFlags()
    {
        using var stream = typeof(ArmorStandInvisibility).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.invisibility.json")!;
        using var fixture = JsonDocument.Parse(stream);
        await using var context = TestServerFactory.CreateServer($"invisibility-reference-{Guid.NewGuid():N}");
        await FixtureCases.AssertAllAsync(fixture.RootElement.GetProperty("cases").EnumerateArray(),
            scenario => scenario.GetProperty("name").GetString()!, async scenario =>
            {
                Living entity = scenario.GetProperty("entity").GetString() switch
                {
                    "player" => new ServerPlayer(Guid.NewGuid(), "VisibilityReference", null!, context.World)
                    {
                        GameMode = GameMode.Survival
                    },
                    "armor_stand" => new ArmorStand { Level = context.World },
                    "pig" => new Pig { Level = context.World },
                    _ => throw new InvalidOperationException("Unknown capture entity")
                };
                entity.Invisible = scenario.GetProperty("invisible").GetBoolean();
                foreach (var step in scenario.GetProperty("steps").EnumerateArray())
                {
                    var action = step.GetProperty("action").GetString();
                    switch (action)
                    {
                        case "initial":
                            break;
                        case "add":
                            entity.AddPotionEffect(InvisibilityId, 200);
                            break;
                        case "replace":
                            entity.AddPotionEffect(InvisibilityId, 2, amplifier: 1);
                            break;
                        case "remove":
                            entity.RemovePotionEffect(InvisibilityId);
                            break;
                        case "add_short":
                            entity.AddPotionEffect(InvisibilityId, 1);
                            break;
                        case "expire":
                            await entity.TickAsync();
                            break;
                        case "spectator":
                            ((ServerPlayer)entity).GameMode = GameMode.Spectator;
                            break;
                        case "survival":
                            ((ServerPlayer)entity).GameMode = GameMode.Survival;
                            break;
                        default:
                            throw new InvalidOperationException($"Unknown capture action {action}");
                    }

                    using var metadata = new NetworkBuffer();
                    entity.Write(metadata);
                    metadata.Offset = 0;
                    Assert.Equal(0, metadata.ReadByte());
                    Assert.Equal((int)EntityMetadataType.Byte, metadata.ReadVarInt());
                    var actual = metadata.ReadByte();
                    var expected = step.GetProperty("flags").GetInt32();
                    Assert.True(actual == expected, $"{action}: vanilla flag byte {expected}, Obsidian {actual}");
                }
            });
    }
}
