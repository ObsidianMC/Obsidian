using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Structures;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Structure block tests that point vanilla's template registry somewhere else for a while, which run alone: other
/// tests loading a vanilla template meanwhile would cache it empty for the rest of the run. Alone, they also keep clear
/// of the plugin tag file tests' plugin registries.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StructureTemplateRegistryCollection
{
    public const string Name = "Structure template registry";
}

[Collection(StructureTemplateRegistryCollection.Name)]
public sealed class StructureBlockReviewTests
{
    private static readonly Vector source = new(8, 64, 8);
    private static readonly Vector size = new(1, 1, 1);
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    [Fact]
    public async Task StructureBlockLoadRejectsEscapedResourcePath()
    {
        await using var context = await StartAsync();
        using var resource = new EscapedResource(context.World, Template(context.World, BlocksRegistry.Stone));
        context.World.BlockUpdates.SetBlock(source, BlocksRegistry.StructureBlock, Quiet);
        var settings = new StructureBlockSettings
        {
            Name = resource.Name,
            Mode = StructureBlockMode.Load,
            Offset = Vector.Up,
            Size = size
        };

        var feedback = await StructureBlocks.EditAsync(context.World, source, settings, 2, resource.Name);

        Assert.Equal($"Structure '{resource.Name}' is not available", feedback);
        Assert.True(context.World.BlockUpdates.GetBlock(source + Vector.Up).IsAir);
        Assert.Null(await StructureBlockTemplates.For(context.World).ReadAsync(resource.Name));
    }

    [Fact]
    public async Task PlaceCommandRejectsEscapedResourcePath()
    {
        await using var context = await StartAsync();
        using var resource = new EscapedResource(context.World, Template(context.World, BlocksRegistry.Stone));
        var (command, heard) = await CommandAsync(context, $"place template {resource.Name} 3 64 3");

        Assert.False(command.Succeeded);
        Assert.Equal(0, command.Result);
        Assert.Contains($"Invalid template '{resource.Name}'", heard.Messages);
        Assert.True(context.World.BlockUpdates.GetBlock(new Vector(3, 64, 3)).IsAir);
    }

    [Fact]
    public async Task EvictedReadCompletesWithoutPublishingOrRetrying()
    {
        await using var context = await StartAsync();
        var old = Template(context.World, BlocksRegistry.Stone);
        var replacement = Template(context.World, BlocksRegistry.Dirt);
        var cases = new (StructureTemplate? First, bool Overlap)[]
        {
            (old, false), (null, false), (old, true), (null, true)
        };
        foreach (var (first, overlap) in cases)
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var refillEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var refillRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = 0;
            var templates = new StructureBlockTemplates(context.World.WorldFolderPath, async _ =>
            {
                var read = Interlocked.Increment(ref reads);
                if (read == 1)
                {
                    entered.SetResult();
                    await release.Task;
                    return first;
                }

                if (overlap && read == 2)
                {
                    refillEntered.SetResult();
                    await refillRelease.Task;
                }

                return replacement;
            });

            var pending = templates.ReadAsync("test:evicted");
            await entered.Task;
            templates.Remove("test:evicted");
            Task<StructureTemplate?>? refill = null;
            try
            {
                if (overlap)
                {
                    refill = templates.ReadAsync("test:evicted");
                    await refillEntered.Task;
                }

                release.SetResult();
                // Eviction cannot extend this caller's read, republish a miss or overwrite a pending refill.
                Assert.Same(first, await pending);
                Assert.Null(templates.Cached("test:evicted"));
            }
            finally
            {
                release.TrySetResult();
                refillRelease.TrySetResult();
                if (refill is not null)
                    await refill;
            }

            Assert.Same(replacement, await templates.ReadAsync("test:evicted"));
            Assert.Same(replacement, templates.Cached("test:evicted"));
        }
    }

    [Fact]
    public async Task StructureBlockLoadNotifiesPluginNeighborsOnlyWhenNonStrict()
    {
        await using var context = await StartAsync();
        await using var manager = BlockUpdates.CreatePluginManager(context);
        var archive = Path.Combine(context.World.WorldFolderPath, "probe.obby");
        await File.WriteAllBytesAsync(archive, PluginLoading.ProbeArchive("1.0.0"));
        Assert.NotNull(await manager.LoadPluginAsync(archive));
        Assert.True(BlocksRegistry.TryGetPluginBlock("probe:relay", out var relay));

        var world = context.World;
        StructureBlockTemplates.For(world).Put("test:plugin", Template(world, BlocksRegistry.Stone));
        var editor = new Vector(1, 64, 1);
        var target = new Vector(3, 64, 3);
        var neighbor = target + new Vector(1, 0, 0);
        world.BlockUpdates.SetBlock(editor, BlocksRegistry.StructureBlock, Quiet);
        world.BlockUpdates.SetBlock(neighbor, relay.DefaultState, Quiet);
        var heard = new List<NeighborChangedContext>();
        var previousHook = AppContext.GetData(ProbeRelay.NeighborHookKey);
        AppContext.SetData(ProbeRelay.NeighborHookKey, (Action<NeighborChangedContext>)heard.Add);
        try
        {
            foreach (var strict in new[] { true, false })
            {
                world.BlockUpdates.SetBlock(target, BlocksRegistry.Air, Quiet);
                heard.Clear();
                var settings = new StructureBlockSettings
                {
                    Name = "test:plugin",
                    Mode = StructureBlockMode.Load,
                    Offset = target - editor,
                    Size = size,
                    Strict = strict
                };

                var feedback = await StructureBlocks.EditAsync(world, editor, settings, 2, settings.Name);

                Assert.Equal("Structure loaded from 'test:plugin'", feedback);
                Assert.True(world.BlockUpdates.GetBlock(target).Is(BlockIds.Stone));
                var notified = heard.Exists(change => change.Position == neighbor
                    && change.SourcePosition == target && change.SourceBlock.Is(BlockIds.Stone));
                Assert.Equal(!strict, notified);
            }
        }
        finally
        {
            AppContext.SetData(ProbeRelay.NeighborHookKey, previousHook);
        }
    }

    [Fact]
    public async Task PlaceCommandRejectsUnloadedInclusiveAreaWithoutGeneratingIt()
    {
        await using var context = await StartAsync();
        var template = Template(context.World, BlocksRegistry.Stone);
        StructureBlockTemplates.For(context.World).Put("test:loaded", template);
        Assert.Null(context.World.GetLoadedChunk(1, 0));

        // Vanilla checks the unrotated pos..pos+size range, including the extra endpoint chunk at x=16.
        var (edge, edgeFeedback) = await CommandAsync(context,
            "place template test:loaded 15 64 3 none none 1 42 strict");
        var (distant, distantFeedback) = await CommandAsync(context,
            "place template test:loaded 128 64 128");

        Assert.False(edge.Succeeded);
        Assert.False(distant.Succeeded);
        Assert.Contains("That position is not loaded", edgeFeedback.Messages);
        Assert.Contains("That position is not loaded", distantFeedback.Messages);
        Assert.True(context.World.BlockUpdates.GetBlock(new Vector(15, 64, 3)).IsAir);
        Assert.Null(context.World.GetLoadedChunk(1, 0));
        Assert.Null(context.World.GetLoadedChunk(8, 8));
    }

    private static StructureTemplate Template(World world, IBlock block)
    {
        world.BlockUpdates.SetBlock(source, block, Quiet);
        return StructureTemplate.Capture(world, source, size, entities: false, author: "review");
    }

    private static async Task<RealServerContext> StartAsync()
    {
        var context = TestServerFactory.CreateServer($"structure-block-review-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World, 0, 0);
        await context.World.GetChunkAsync(0, 0, scheduleGeneration: false);
        ((CommandHandler)context.Server.CommandHandler)
            .RegisterCommandClass<Obsidian.Commands.Modules.PlaceCommandModule>(null);
        return context;
    }

    private static async Task<(CommandContext, Feedback)> CommandAsync(RealServerContext context, string text)
    {
        var heard = new Feedback();
        var command = new CommandContext("/" + text, heard, null, context.Server);
        await ((CommandHandler)context.Server.CommandHandler).ProcessCommand(command);
        return (command, heard);
    }

    private sealed class Feedback : ICommandSender
    {
        public CommandIssuers Issuer => CommandIssuers.Console;

        public IPlayer? Player => null;

        public List<string> Messages { get; } = [];

        public Task SendMessageAsync(ChatMessage message)
        {
            this.Messages.Add(StoredCommand.PlainText(message));
            return Task.CompletedTask;
        }

        public Task SendMessageAsync(ChatMessage message, Guid sender) => this.SendMessageAsync(message);
    }

    // Plant a valid template outside the isolated resource root. Restoring the published directory keeps other
    // tests on their original extracted resources; unique ids keep this probe out of their template cache.
    private sealed class EscapedResource : IDisposable
    {
        private readonly FieldInfo field = typeof(StructureRegistry)
            .GetField("currentDirectory", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? previous;

        public string Name { get; }

        public EscapedResource(World world, StructureTemplate template)
        {
            this.previous = this.field.GetValue(null);
            var root = Path.Combine(world.WorldFolderPath, "resources", "templates");
            Directory.CreateDirectory(root);
            var name = "escape-" + Guid.NewGuid().ToString("N");
            using (var stream = File.Create(Path.Combine(world.WorldFolderPath, name + ".nbt")))
                template.Save(stream);

            this.Name = "minecraft:../../" + name;
            StructureRegistry.InitializeCurrent(root);
        }

        public void Dispose() => this.field.SetValue(null, this.previous);
    }
}
