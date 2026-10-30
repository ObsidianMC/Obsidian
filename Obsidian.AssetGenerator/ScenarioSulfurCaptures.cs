using System.Text.Json.Nodes;

namespace Obsidian.AssetGenerator;

internal static class ScenarioSulfurCaptures
{
    internal const string Notes = """
        Recorded from a frozen vanilla 26.3 server over RCON, seed test-seed. Setup and actions use setblock with
        updates. Each checkpoint records absolute game time, full states at watched positions and potent sulfur's
        saved countdown. Reload actions save and restart vanilla; their elapsed game ticks are retained. Other
        actions run before the next tick. Only this fixture is recorded by CaptureFixtures=sulfur.
        """;

    internal static async Task CaptureAsync(ScenarioServer vanilla, JsonObject root)
    {
        const string dimension = ScenarioServer.Overworld;
        await vanilla.RunInAsync(dimension, "forceload add -16 -16 31 31");
        foreach (var entry in root["scenarios"]!.AsArray())
        {
            var scenario = entry!.AsObject();
            Console.WriteLine($"Capturing sulfur scenario {scenario["name"]}...");
            await vanilla.RunInAsync(dimension, "fill -8 70 -8 15 95 15 air strict");
            for (var drain = 0; drain < 40; drain++)
            {
                await vanilla.StepAsync();
            }

            foreach (var setup in scenario["setup"]!.AsArray())
            {
                await PlaceAsync(vanilla, setup!);
            }

            var checkpoints = new JsonArray();
            checkpoints.Add(await SampleAsync(vanilla, scenario, 0));
            var ticks = scenario["ticks"]!.GetValue<int>();
            for (var tick = 1; tick <= ticks; tick++)
            {
                foreach (var action in scenario["actions"]!.AsArray()
                    .Where(action => action!["tick"]!.GetValue<int>() == tick))
                {
                    if (action!["reload"] is not null)
                    {
                        var before = await vanilla.GameTimeAsync();
                        await vanilla.RestartAsync();
                        action["elapsed"] = await vanilla.GameTimeAsync() - before;
                    }
                    else if (action["countdown"] is not null)
                    {
                        await vanilla.RunInAsync(dimension,
                            $"data merge block {PositionOf(action["pos"]!)} {{countdown:{action["countdown"]}}}");
                    }
                    else
                    {
                        await PlaceAsync(vanilla, action);
                    }
                }

                await vanilla.StepAsync();
                if (tick % scenario["interval"]!.GetValue<int>() == 0 || tick == ticks)
                {
                    checkpoints.Add(await SampleAsync(vanilla, scenario, tick));
                }
            }

            scenario["checkpoints"] = checkpoints;
        }
    }

    private static Task<string> PlaceAsync(ScenarioServer vanilla, JsonNode placement) =>
        vanilla.RunInAsync(ScenarioServer.Overworld,
            $"setblock {PositionOf(placement["pos"]!)} {placement["state"]!.GetValue<string>()}");

    private static async Task<JsonObject> SampleAsync(ScenarioServer vanilla, JsonObject scenario, int tick)
    {
        var states = new JsonArray();
        var countdowns = new JsonArray();
        foreach (var watched in scenario["watch"]!.AsArray())
        {
            var position = PositionOf(watched!);
            var state = await vanilla.ReadStateAsync(ScenarioServer.Overworld, position, null,
                ["minecraft:potent_sulfur", "minecraft:sulfur_spike", "minecraft:air", "minecraft:water"]);
            states.Add(state);
            if (state.StartsWith("minecraft:potent_sulfur", StringComparison.Ordinal))
            {
                var feedback = await vanilla.RunInAsync(
                    ScenarioServer.Overworld,
                    $"data get block {position} countdown");
                var number = feedback[(feedback.LastIndexOf(' ') + 1)..].TrimEnd('.');
                countdowns.Add(int.Parse(number, System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                countdowns.Add((JsonNode?)null);
            }
        }

        return new JsonObject
        {
            ["tick"] = tick,
            ["gameTime"] = await vanilla.GameTimeAsync(),
            ["states"] = states,
            ["countdowns"] = countdowns
        };
    }

    private static Position PositionOf(JsonNode value) =>
        new(value[0]!.GetValue<int>(), value[1]!.GetValue<int>(), value[2]!.GetValue<int>());
}
