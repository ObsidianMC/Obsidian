using Obsidian.API.Builders;
using System.Diagnostics.CodeAnalysis;
using Obsidian.API.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Net.Packets.Play.Clientbound;
using static Obsidian.Commands.CommandFeedback;

namespace Obsidian.Commands.Modules;

/// <summary>
/// Vanilla's commands that change or query the world: <c>/setblock</c>, <c>/fill</c>, <c>/clone</c>,
/// <c>/weather</c>, <c>/difficulty</c>, <c>/defaultgamemode</c>, <c>/setworldspawn</c>, <c>/seed</c>,
/// <c>/particle</c> and <c>/playsound</c>.
/// </summary>
[RequirePermissionLevel(2)]
public sealed class WorldCommandModule : CommandModuleBase
{
    // Vanilla's limit for /fill and /clone (the commandModificationBlockLimit game rule's default).
    internal const int MaxBlocks = 32768;

    public enum SetBlockMode
    {
        Replace,
        Destroy,
        Keep
    }

    public enum FillMode
    {
        Replace,
        Destroy,
        Keep,
        Hollow,
        Outline
    }

    public enum CloneMode
    {
        Replace,
        Masked
    }

    public enum Weather
    {
        Clear,
        Rain,
        Thunder
    }

    public enum ParticleMode
    {
        Normal,
        Force
    }

    // The sources /playsound takes, named like vanilla's, in the order of SoundCategory.
    public enum SoundSource
    {
        Master,
        Music,
        Record,
        Weather,
        Block,
        Hostile,
        Neutral,
        Player,
        Ambient,
        Voice
    }

    [Command("setblock")]
    [CommandInfo("Changes a block.", "/setblock <pos> <block> [destroy|keep|replace]")]
    public Task SetBlockAsync(Vector pos, BlockStateArgument block) => this.SetBlockAsync(pos, block, SetBlockMode.Replace);

    [CommandOverload]
    public async Task SetBlockAsync(Vector pos, BlockStateArgument block, SetBlockMode mode)
    {
        if (await this.ResolveAsync(block) is not IBlock state)
            return;

        var level = LevelOf(this.CommandContext);
        if (!await IsLoadedAsync(level, pos))
        {
            await this.CommandContext.FailAsync("That position is not loaded");
            return;
        }

        var existing = await level.GetBlockAsync(pos);
        if (mode == SetBlockMode.Keep && existing is { IsAir: false } || existing is not null && SameState(existing, state))
        {
            await this.CommandContext.FailAsync("Could not set the block");
            return;
        }

        if (mode == SetBlockMode.Destroy)
            level.DestroyBlock(pos, dropItems: true);

        await level.SetBlockAsync(pos, state, doBlockUpdate: true);
        await this.CommandContext.SuccessAsync($"Changed the block at {Format(pos)}");
    }

    [Command("fill")]
    [CommandInfo("Fills a region with a block.", "/fill <from> <to> <block> [destroy|hollow|keep|outline|replace [<filter>]]")]
    public Task FillAsync(Vector from, Vector to, BlockStateArgument block) => this.FillAsync(from, to, block, FillMode.Replace, null);

    [CommandOverload]
    public Task FillAsync(Vector from, Vector to, BlockStateArgument block, FillMode mode) => this.FillAsync(from, to, block, mode, null);

    [CommandOverload]
    public async Task FillAsync(Vector from, Vector to, BlockStateArgument block, FillMode mode, BlockStateArgument? filter)
    {
        if (filter is not null && mode != FillMode.Replace)
        {
            await this.CommandContext.FailAsync("Only replace takes a filter");
            return;
        }

        if (await this.ResolveAsync(block) is not IBlock state)
            return;

        var (min, max) = Bounds(from, to);
        var volume = Volume(min, max);
        if (volume > MaxBlocks)
        {
            await this.CommandContext.FailAsync($"Too many blocks in the specified area (maximum {MaxBlocks}, specified {volume})");
            return;
        }

        var level = LevelOf(this.CommandContext);
        if (!await IsLoadedAsync(level, min, max))
        {
            await this.CommandContext.FailAsync("That position is not loaded");
            return;
        }

        var air = BlocksRegistry.Air;
        var changed = 0;

        for (var x = min.X; x <= max.X; x++)
        for (var y = min.Y; y <= max.Y; y++)
        for (var z = min.Z; z <= max.Z; z++)
        {
            var position = new Vector(x, y, z);
            var edge = x == min.X || x == max.X || y == min.Y || y == max.Y || z == min.Z || z == max.Z;

            // Hollow fills the inside with air; outline leaves it alone.
            var target = mode switch
            {
                FillMode.Hollow when !edge => air,
                FillMode.Outline when !edge => null,
                _ => state
            };

            if (target is null || await level.GetBlockAsync(position) is not IBlock existing)
                continue;

            if (mode == FillMode.Keep && !existing.IsAir || filter is not null && !Matches(existing, filter) || SameState(existing, target))
                continue;

            if (mode == FillMode.Destroy)
                level.DestroyBlock(position, dropItems: true);

            await level.SetBlockAsync(position, target, doBlockUpdate: true);
            changed++;
        }

        if (changed == 0)
            await this.CommandContext.FailAsync("No blocks were filled");
        else
            await this.CommandContext.SuccessAsync($"Successfully filled {changed} block(s)");
    }

    [Command("clone")]
    [CommandInfo("Copies blocks from one place to another.", "/clone <begin> <end> <destination> [replace|masked]")]
    public Task CloneAsync(Vector begin, Vector end, Vector destination) => this.CloneAsync(begin, end, destination, CloneMode.Replace);

    [CommandOverload]
    public async Task CloneAsync(Vector begin, Vector end, Vector destination, CloneMode mode)
    {
        var (min, max) = Bounds(begin, end);
        var volume = Volume(min, max);
        if (volume > MaxBlocks)
        {
            await this.CommandContext.FailAsync($"Too many blocks in the specified area (maximum {MaxBlocks}, specified {volume})");
            return;
        }

        var size = max - min;
        var destinationMax = destination + size;

        // Like vanilla's without the force mode, the areas mustn't overlap.
        var overlaps = destination.X <= max.X && destinationMax.X >= min.X
            && destination.Y <= max.Y && destinationMax.Y >= min.Y
            && destination.Z <= max.Z && destinationMax.Z >= min.Z;
        if (overlaps)
        {
            await this.CommandContext.FailAsync("The source and destination areas cannot overlap");
            return;
        }

        var level = LevelOf(this.CommandContext);
        if (!await IsLoadedAsync(level, min, max) || !await IsLoadedAsync(level, destination, destinationMax))
        {
            await this.CommandContext.FailAsync("That position is not loaded");
            return;
        }

        // Read the whole source first, as vanilla does.
        var blocks = new List<(Vector Offset, IBlock Block)>(volume);
        for (var x = min.X; x <= max.X; x++)
        for (var y = min.Y; y <= max.Y; y++)
        for (var z = min.Z; z <= max.Z; z++)
        {
            var position = new Vector(x, y, z);
            if (await level.GetBlockAsync(position) is IBlock block && !(mode == CloneMode.Masked && block.IsAir))
                blocks.Add((position - min, block));
        }

        var changed = 0;
        foreach (var (offset, block) in blocks)
        {
            var target = destination + offset;
            if (await level.GetBlockAsync(target) is IBlock existing && SameState(existing, block))
                continue;

            await level.SetBlockAsync(target, block, doBlockUpdate: true);
            changed++;
        }

        if (changed == 0)
            await this.CommandContext.FailAsync("No blocks were cloned");
        else
            await this.CommandContext.SuccessAsync($"Successfully cloned {changed} block(s)");
    }

    [Command("weather")]
    [CommandInfo("Sets the weather.", "/weather <clear|rain|thunder> [<duration>]")]
    public Task WeatherAsync(Weather weather) => this.WeatherAsync(weather, -1);

    [CommandOverload]
    public async Task WeatherAsync(Weather weather, int duration)
    {
        var level = LevelOf(this.CommandContext);
        var data = level.LevelData;

        // Seconds, or vanilla's default of a random 5 to 15 minutes for rain and thunder, and clear skies.
        var ticks = duration > 0 ? duration * 20 : weather == Weather.Clear ? Random.Shared.Next(12_000, 180_000) : Random.Shared.Next(3_600, 15_600);

        var wasRaining = data.Raining;
        data.Raining = weather != Weather.Clear;
        data.Thundering = weather == Weather.Thunder;
        data.ClearWeatherTime = weather == Weather.Clear ? ticks : 0;
        data.RainTime = data.Raining ? ticks : 0;
        data.ThunderTime = data.Thundering ? ticks : 0;

        var broadcaster = level.PacketBroadcaster;
        if (wasRaining != data.Raining)
            broadcaster.QueuePacketToLevel(level, new GameEventPacket(data.Raining ? ChangeGameStateReason.BeginRaining : ChangeGameStateReason.EndRaining));

        broadcaster.QueuePacketToLevel(level, new GameEventPacket(ChangeGameStateReason.RainLevelChange, data.Raining ? 1 : 0));
        broadcaster.QueuePacketToLevel(level, new GameEventPacket(ChangeGameStateReason.ThunderLevelChange, data.Thundering ? 1 : 0));

        await this.CommandContext.SuccessAsync(weather switch
        {
            Weather.Clear => "Set the weather to clear",
            Weather.Rain => "Set the weather to rain",
            _ => "Set the weather to rain & thunder"
        });
    }

    [Command("difficulty")]
    [CommandInfo("Sets or queries the difficulty.", "/difficulty [peaceful|easy|normal|hard]")]
    public Task DifficultyAsync() =>
        this.CommandContext.SuccessAsync($"The difficulty is {LevelOf(this.CommandContext).LevelData.Difficulty}");

    [CommandOverload]
    public async Task DifficultyAsync(Difficulty difficulty)
    {
        var levels = this.Server.WorldManager.GetAvailableWorlds();
        if (levels.All(world => world.LevelData.Difficulty == difficulty))
        {
            await this.CommandContext.FailAsync($"The difficulty did not change; it is already set to {difficulty}");
            return;
        }

        foreach (var world in levels)
        {
            world.LevelData.Difficulty = difficulty;
            world.PacketBroadcaster.QueuePacketToLevel(world, ChangeDifficultyPacket.Of(world.LevelData));
        }

        foreach (var player in this.Server.OnlinePlayers.Values)
            await player.QueuePacketAsync(ChangeDifficultyPacket.Of(player.Level.LevelData));

        await this.CommandContext.SuccessAsync($"The difficulty has been set to {difficulty}");
    }

    [Command("defaultgamemode")]
    [CommandInfo("Sets the game mode new players start in.", "/defaultgamemode <gamemode>")]
    public async Task DefaultGameModeAsync(GameMode gamemode)
    {
        foreach (var world in this.Server.WorldManager.GetAvailableWorlds())
            world.LevelData.DefaultGamemode = gamemode;

        await this.CommandContext.SuccessAsync($"The default game mode is now {gamemode} Mode");
    }

    [Command("setworldspawn")]
    [CommandInfo("Sets the world spawn.", "/setworldspawn [<pos>]")]
    public Task SetWorldSpawnAsync() => this.SetWorldSpawnAsync((Vector)OriginOf(this.CommandContext).Floor());

    [CommandOverload]
    public async Task SetWorldSpawnAsync(Vector pos)
    {
        var level = LevelOf(this.CommandContext);
        level.LevelData.SpawnPosition = (VectorD)pos + new VectorD(0.5, 0, 0.5);
        await level.SaveAsync();

        level.PacketBroadcaster.QueuePacketToLevel(level,
            new SetDefaultSpawnPositionPacket(new() { Dimension = level.DimensionName, Pos = pos }, 0, 0));

        await this.CommandContext.SuccessAsync($"Set the world spawn point to {Format(pos)}");
    }

    [Command("seed")]
    [CommandInfo("Shows the world seed.", "/seed")]
    public Task SeedAsync() => this.CommandContext.SuccessAsync($"Seed: [{LevelOf(this.CommandContext).Seed}]");

    [Command("particle")]
    [CommandInfo("Shows particles.", "/particle <name> [<pos>] [<delta> <speed> <count> [force|normal]]")]
    public Task ParticleAsync(ParticleType name) => this.ParticleAsync(name, OriginOf(this.CommandContext));

    [CommandOverload]
    public Task ParticleAsync(ParticleType name, VectorD pos) => this.ShowParticlesAsync(name, pos, default, 0, 0, ParticleMode.Normal);

    [CommandOverload]
    public Task ParticleAsync(ParticleType name, VectorD pos, VectorD delta, float speed, int count) =>
        this.ShowParticlesAsync(name, pos, delta, speed, count, ParticleMode.Normal);

    [CommandOverload]
    public Task ParticleAsync(ParticleType name, VectorD pos, VectorD delta, float speed, int count, ParticleMode mode) =>
        this.ShowParticlesAsync(name, pos, delta, speed, count, mode);

    private async Task ShowParticlesAsync(ParticleType type, VectorD pos, VectorD delta, float speed, int count, ParticleMode mode)
    {
        if (!SimpleParticle.TryCreate(type, out var particle))
        {
            await this.CommandContext.FailAsync($"The particle {type} needs options, which /particle doesn't support yet");
            return;
        }

        if (speed < 0 || count < 0)
        {
            await this.CommandContext.FailAsync("The speed and count can't be negative");
            return;
        }

        // A delta of a vec3 argument is read like a position; whole numbers name the block's center.
        var spread = new VectorF((float)delta.X, (float)delta.Y, (float)delta.Z);

        LevelOf(this.CommandContext).SpawnParticles(particle, pos, Math.Max(count, 0), spread, speed, mode == ParticleMode.Force);
        await this.CommandContext.SuccessAsync($"Displaying particle minecraft:{type.ToString().ToSnakeCase()}");
    }

    [Command("playsound")]
    [CommandInfo("Plays a sound.", "/playsound <sound> [<source>] [<targets>] [<pos>] [<volume>] [<pitch>] [<minVolume>]")]
    public Task PlaySoundAsync([ClientSuggestions(ClientSuggestionsAttribute.AvailableSounds)] ResourceLocation sound) =>
        this.PlaySoundAsync(sound, SoundSource.Master);

    [CommandOverload]
    public Task PlaySoundAsync([ClientSuggestions(ClientSuggestionsAttribute.AvailableSounds)] ResourceLocation sound, SoundSource source) =>
        this.Player is IPlayer player
            ? this.PlaySoundAsync(sound, source, [player])
            : this.CommandContext.FailAsync("Name the players to play the sound to");

    [CommandOverload]
    public Task PlaySoundAsync([ClientSuggestions(ClientSuggestionsAttribute.AvailableSounds)] ResourceLocation sound, SoundSource source,
        IPlayer[] targets) =>
        this.PlaySoundAsync(sound, source, targets, OriginOf(this.CommandContext), 1, 1, 0);

    [CommandOverload]
    public Task PlaySoundAsync([ClientSuggestions(ClientSuggestionsAttribute.AvailableSounds)] ResourceLocation sound, SoundSource source,
        IPlayer[] targets, VectorD pos) =>
        this.PlaySoundAsync(sound, source, targets, pos, 1, 1, 0);

    [CommandOverload]
    public Task PlaySoundAsync([ClientSuggestions(ClientSuggestionsAttribute.AvailableSounds)] ResourceLocation sound, SoundSource source,
        IPlayer[] targets, VectorD pos, float volume) =>
        this.PlaySoundAsync(sound, source, targets, pos, volume, 1, 0);

    [CommandOverload]
    public Task PlaySoundAsync([ClientSuggestions(ClientSuggestionsAttribute.AvailableSounds)] ResourceLocation sound, SoundSource source,
        IPlayer[] targets, VectorD pos, float volume, float pitch) =>
        this.PlaySoundAsync(sound, source, targets, pos, volume, pitch, 0);

    [CommandOverload]
    public async Task PlaySoundAsync([ClientSuggestions(ClientSuggestionsAttribute.AvailableSounds)] ResourceLocation sound, SoundSource source,
        IPlayer[] targets, VectorD pos, float volume, float pitch, float minVolume)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        if (volume < 0 || pitch is < 0 or > 2 || minVolume is < 0 or > 1)
        {
            await this.CommandContext.FailAsync("The volume can't be negative, the pitch is 0 to 2 and the minimum volume 0 to 1");
            return;
        }

        // Like vanilla's, a sound is heard 16 blocks away, more for louder ones; players further away hear it at
        // their own position at the minimum volume, or not at all.
        var range = volume > 1 ? volume * 16 : 16;
        var played = 0;

        foreach (var player in targets)
        {
            var at = pos;
            var heard = volume;
            var offset = pos - player.Position;

            if (offset.MagnitudeSquared() > range * range)
            {
                if (minVolume <= 0)
                    continue;

                at = player.Position + offset.Normalize() * 2;
                heard = minVolume;
            }

            var effect = SoundEffectBuilder.Create(sound.ToString(), (SoundCategory)source)
                .WithSoundPosition(new SoundPosition(at.X, at.Y, at.Z))
                .WithVolume(heard)
                .WithPitch(pitch)
                .WithSeed(Random.Shared.NextInt64())
                .Build();

            await player.SendSoundAsync(effect);
            played++;
        }

        if (played == 0)
            await this.CommandContext.FailAsync("The sound is too far away to be heard");
        else
            await this.CommandContext.SuccessAsync($"Played sound {sound} to {Describe(targets, "players")}");
    }

    [Command("stopsound")]
    [CommandInfo("Stops sounds.", "/stopsound <targets> [<source>] [<sound>]")]
    public Task StopSoundAsync(IPlayer[] targets) => this.StopSoundsAsync(targets, null, null);

    [CommandOverload]
    public Task StopSoundAsync(IPlayer[] targets, [Suggestions("*", "master", "music", "record", "weather", "block", "hostile", "neutral", "player", "ambient", "voice")] string source) =>
        this.StopSoundsAsync(targets, source, null);

    [CommandOverload]
    public Task StopSoundAsync(IPlayer[] targets, [Suggestions("*", "master", "music", "record", "weather", "block", "hostile", "neutral", "player", "ambient", "voice")] string source,
        [ClientSuggestions(ClientSuggestionsAttribute.AvailableSounds)] ResourceLocation sound) =>
        this.StopSoundsAsync(targets, source, sound);

    // A source of * stops the sound in every category, as vanilla's does.
    private async Task StopSoundsAsync(IPlayer[] targets, string? source, ResourceLocation? sound)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        SoundCategory? category = null;
        if (source is not (null or "*"))
        {
            if (!Enum.TryParse<SoundSource>(source, ignoreCase: true, out var parsed) || int.TryParse(source, out _))
            {
                await this.CommandContext.FailAsync($"Unknown sound source '{source}'");
                return;
            }

            category = (SoundCategory)parsed;
        }

        var packet = new StopSoundPacket { Category = category, Sound = sound?.ToString() };
        foreach (var player in targets)
            await player.QueuePacketAsync(packet);

        await this.CommandContext.SuccessAsync(sound is null
            ? $"Stopped all {(category is null ? string.Empty : source + " ")}sounds"
            : $"Stopped sound '{sound}'{(category is null ? string.Empty : $" on source '{source}'")}");
    }

    /// <summary>The block a block state argument names, or <see langword="null"/> after telling the sender it's unknown.</summary>
    private async Task<IBlock?> ResolveAsync(BlockStateArgument block)
    {
        if (BlockStateProperties.TryGetState(block.Name, block.Properties, out var state))
            return state;

        await this.CommandContext.FailAsync($"Unknown block type '{block.Name}'");
        return null;
    }

    // Whether a block is the named one, with the properties the filter gives.
    private static bool Matches(IBlock block, BlockStateArgument filter)
    {
        if (block.UnlocalizedName != filter.Name)
            return false;

        foreach (var (name, value) in filter.Properties)
        {
            if (block.GetProperty(name) != value)
                return false;
        }

        return true;
    }

    private static bool SameState(IBlock first, IBlock second) =>
        first.RegistryId == second.RegistryId && first.State?.Id == second.State?.Id;

    internal static (Vector Min, Vector Max) Bounds(Vector first, Vector second) => (
        new Vector(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), Math.Min(first.Z, second.Z)),
        new Vector(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y), Math.Max(first.Z, second.Z)));

    internal static int Volume(Vector min, Vector max) =>
        (int)Math.Min(int.MaxValue, (long)(max.X - min.X + 1) * (max.Y - min.Y + 1) * (max.Z - min.Z + 1));

    // Whether every chunk of a region is loaded, as vanilla requires of the blocks commands change.
    private static async Task<bool> IsLoadedAsync(ILevel level, Vector min, Vector max)
    {
        for (var x = min.X >> 4; x <= max.X >> 4; x++)
        for (var z = min.Z >> 4; z <= max.Z >> 4; z++)
        {
            if (await level.GetChunkAsync(x, z, scheduleGeneration: false) is null)
                return false;
        }

        return true;
    }

    private static Task<bool> IsLoadedAsync(ILevel level, Vector position) => IsLoadedAsync(level, position, position);

    /// <summary>A particle without options, which <c>/particle</c> can show.</summary>
    private sealed class SimpleParticle(ParticleType type) : ParticleData
    {
        public override ParticleType ParticleType { get; } = type;

        public static bool TryCreate(ParticleType type, [NotNullWhen(true)] out SimpleParticle? particle)
        {
            particle = Registries.Codecs.RegistryIds.HasOptions($"minecraft:{type.ToString().ToSnakeCase()}") ? null : new SimpleParticle(type);
            return particle is not null;
        }
    }
}
