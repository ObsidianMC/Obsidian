using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using Obsidian.Hosting;
using Obsidian.Integrated;
using Obsidian.Nbt;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Tests that change the server's static paths, which run alone.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ServerPathsCollection
{
    public const string Name = "Server paths";
}

public class IntegratedProtocolTests
{
    [Fact]
    public void ParsesCommandsAndFormatsEvents()
    {
        Assert.True(IntegratedProtocol.TryParseCommand("""@obsidian:{"command":"publish","port":25565,"gameMode":"creative","allowCommands":true}""",
            out var publish));
        Assert.Equal(new IntegratedCommand { Command = "publish", Port = 25565, GameMode = "creative", AllowCommands = true }, publish);

        Assert.True(IntegratedProtocol.TryParseCommand("""@obsidian:{"command":"save","flush":true}""", out var save));
        Assert.True(save.Flush);

        // Ordinary console commands, broken JSON and objects without a command aren't commands.
        Assert.False(IntegratedProtocol.TryParseCommand("say hello", out _));
        Assert.False(IntegratedProtocol.TryParseCommand("@obsidian:{\"command\":", out _));
        Assert.False(IntegratedProtocol.TryParseCommand("@obsidian:{}", out _));

        Assert.Equal("""@obsidian:{"event":"ready","port":51234}""", IntegratedProtocol.FormatEvent(IntegratedEvent.Ready(51234)));
        Assert.Equal("""@obsidian:{"event":"saving","autosave":true}""", IntegratedProtocol.FormatEvent(IntegratedEvent.Saving(true)));
        Assert.Equal("""@obsidian:{"event":"progress","stage":"generating","percent":42}""",
            IntegratedProtocol.FormatEvent(IntegratedEvent.Progress("generating", 42)));
        Assert.Equal("""@obsidian:{"event":"stats","tickMs":3.5}""", IntegratedProtocol.FormatEvent(IntegratedEvent.Stats(3.4567)));
    }

    [Fact]
    public void JoinSecretMustMatchExactly()
    {
        var session = new IntegratedSession(Options.Create(new IntegratedConfiguration
        {
            Enabled = true,
            LocalPlayerName = "Dev",
            JoinSecret = "s3cret-☃"
        }));

        Assert.True(session.VerifyJoinSecret(Encoding.UTF8.GetBytes("s3cret-☃")));
        Assert.False(session.VerifyJoinSecret(Encoding.UTF8.GetBytes("s3cret-x")));
        Assert.False(session.VerifyJoinSecret(Encoding.UTF8.GetBytes("s3cret")));
        Assert.False(session.VerifyJoinSecret([]));

        // The name is reserved without regard to case, like vanilla's names.
        Assert.True(session.IsLocalPlayer("dev"));
        Assert.False(session.IsLocalPlayer("Steve"));

        // Without a configured secret nobody can join as the local player.
        var unconfigured = new IntegratedSession(Options.Create(new IntegratedConfiguration { Enabled = true, LocalPlayerName = "Dev" }));
        Assert.False(unconfigured.VerifyJoinSecret([]));
    }
}

