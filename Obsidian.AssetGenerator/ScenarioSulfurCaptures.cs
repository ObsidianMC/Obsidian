using System.Text.Json.Nodes;

namespace Obsidian.AssetGenerator;

internal static class ScenarioSulfurCaptures
{
    internal const string Notes = """
        Recorded from a frozen vanilla 26.3 server over RCON, seed test-seed. Setup and actions use setblock with
        updates. Each checkpoint records absolute game time, full states at watched positions and potent sulfur's
        saved countdown. Reload actions save and restart vanilla; their elapsed game ticks are retained. Other
        actions run before the next tick. Optional entity samples count live falling blocks, damaging falling blocks
        and total sulfur-spike items, and projectile samples count embedded projectiles. These samples do not record
        sound effects. CaptureCases selects individual scenarios without re-recording the Java probes.
        """;

    internal static async Task CaptureAsync(ScenarioServer vanilla, JsonObject root,
        IReadOnlyCollection<string>? cases = null)
    {
        const string dimension = ScenarioServer.Overworld;
        await vanilla.RunInAsync(dimension, "forceload add -16 -16 31 31");
        await vanilla.RunInAsync(dimension, "scoreboard objectives add sulfur_capture dummy");
        var scenarios = root["scenarios"]!.AsArray()
            .Where(entry => cases is null || cases.Contains(entry!["name"]!.GetValue<string>())).ToArray();
        if (cases is not null && scenarios.Length != cases.Count)
            throw new ArgumentException("Every selected sulfur scenario must exist and be named once.");

        foreach (var entry in scenarios)
        {
            var scenario = entry!.AsObject();
            Console.WriteLine($"Capturing sulfur scenario {scenario["name"]}...");
            await vanilla.RunInAsync(dimension, "fill -8 70 -8 15 95 15 air strict");
            await vanilla.RunInAsync(dimension, "gamerule minecraft:entity_drops true");
            for (var drain = 0; drain < 40; drain++)
            {
                await vanilla.StepAsync();
            }

            await vanilla.RunInAsync(dimension, "kill @e[type=!minecraft:player]");
            if (scenario["rules"] is JsonObject rules)
            {
                foreach (var (name, value) in rules)
                {
                    await vanilla.RunInAsync(dimension, $"gamerule minecraft:{name} {value}");
                }
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
                    else if (action["projectile"] is JsonObject projectile)
                    {
                        await ShootAsync(vanilla, projectile);
                    }
                    else if (action["fallingFlags"] is JsonObject flags)
                    {
                        var fields = string.Join(",", flags.Select(field => $"{field.Key}:{field.Value}b"));
                        await vanilla.RunInAsync(dimension,
                            $"execute as @e[type=minecraft:falling_block] run data merge entity @s {{{fields}}}");
                    }
                    else
                    {
                        await PlaceAsync(vanilla, action);
                    }
                }

                await vanilla.StepAsync();
                var extraSample = scenario["sampleTicks"] is JsonArray samples
                    && samples.Any(sample => sample!.GetValue<int>() == tick);
                if (tick % scenario["interval"]!.GetValue<int>() == 0 || tick == ticks || extraSample)
                {
                    checkpoints.Add(await SampleAsync(vanilla, scenario, tick));
                }
            }

            scenario["checkpoints"] = checkpoints;
        }
    }

    private static Task<string> ShootAsync(ScenarioServer vanilla, JsonObject projectile)
    {
        var position = string.Join(" ", projectile["pos"]!.AsArray());
        var motion = string.Join(",", projectile["motion"]!.AsArray().Select(value => $"{value}d"));
        var type = projectile["type"]!.GetValue<string>();
        return vanilla.RunInAsync(ScenarioServer.Overworld,
            $"summon minecraft:{type} {position} {{Tags:[\"sulfur_projectile\"],NoGravity:1b,Motion:[{motion}]}}");
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

        var checkpoint = new JsonObject
        {
            ["tick"] = tick,
            ["gameTime"] = await vanilla.GameTimeAsync(),
            ["states"] = states,
            ["countdowns"] = countdowns
        };
        if (scenario["recordEntities"]?.GetValue<bool>() == true)
        {
            checkpoint["falling"] = await CountAsync(vanilla, "@e[type=minecraft:falling_block]");
            checkpoint["hurtingFalling"] = await CountAsync(vanilla,
                "@e[type=minecraft:falling_block,nbt={HurtEntities:1b}]");
            const string items = "@e[type=minecraft:item,nbt={Item:{id:\"minecraft:sulfur_spike\"}}]";
            await vanilla.RunInAsync(ScenarioServer.Overworld, "scoreboard players set total sulfur_capture 0");
            await vanilla.RunInAsync(ScenarioServer.Overworld,
                $"execute as {items} store result score @s sulfur_capture run data get entity @s Item.count");
            await vanilla.RunInAsync(ScenarioServer.Overworld,
                $"execute as {items} run scoreboard players operation total sulfur_capture += @s sulfur_capture");
            checkpoint["items"] = await ScoreAsync(vanilla, "total");
        }

        if (scenario["recordProjectile"]?.GetValue<bool>() == true)
        {
            checkpoint["embedded"] = await CountAsync(vanilla,
                "@e[tag=sulfur_projectile,nbt={inGround:1b}]");
        }

        return checkpoint;
    }

    private static async Task<int> CountAsync(ScenarioServer vanilla, string selector)
    {
        await vanilla.RunInAsync(ScenarioServer.Overworld,
            $"execute store result score total sulfur_capture run execute if entity {selector}");
        return await ScoreAsync(vanilla, "total");
    }

    private static async Task<int> ScoreAsync(ScenarioServer vanilla, string name)
    {
        var feedback = await vanilla.RunInAsync(ScenarioServer.Overworld,
            $"scoreboard players get {name} sulfur_capture");
        var number = feedback.Split(' ').Single(word => int.TryParse(word, out _));
        return int.Parse(number, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Position PositionOf(JsonNode value) =>
        new(value[0]!.GetValue<int>(), value[1]!.GetValue<int>(), value[2]!.GetValue<int>());
}
