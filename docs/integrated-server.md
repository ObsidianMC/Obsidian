# Integrated server

A game client can run Obsidian as its integrated server: one server process per open singleplayer world, controlled
over standard input and output, with the player connecting over a loopback TCP connection.

## Launching

The host is the usual one; with `Integrated:Enabled=true`, `ConfigureObsidian` and `AddObsidian` set up everything
else, so no extra extension method is needed:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.ConfigureObsidian();
builder.AddObsidian();
await builder.Build().RunAsync();
```

Settings come from `IConfiguration`: pass them as command-line arguments (`--Key=value`, which override every other
source) and the join secret as the environment variable `Integrated__JoinSecret`, so it isn't visible in the process
list. The process exits with code 0 after a graceful stop and 1 after a crash (set through `Environment.ExitCode`, so
`Main` mustn't return its own code).

| Key | Default | Meaning |
| --- | --- | --- |
| `Paths:Root` | working directory | Root of every server-side path: `config/`, `logs/`, `persistentdata/`, `permissions/`, `plugins/`, `accepted_keys/`, `usercache.json`. Without `Integrated:WorldPath`, also `worlds/`. |
| `Paths:Cache` | `<root>/cache` | Vanilla data extracted from Mojang's server jar (structure templates), downloaded once. Point every world's server at one shared folder. Two servers extracting into an empty cache at the same time can race, so start one first. |
| `Paths:Logs` | `<root>/logs` | Log files, one per start. |
| `Integrated:Enabled` | `false` | Runs as an integrated server. |
| `Integrated:WorldPath` | none | The world folder (see [World](#world)). Without it, the worlds come from `config/worlds.json` as on a dedicated server. |
| `Integrated:LocalPlayerName` | none | The client's player name, reserved for the client (see [Joining](#joining)). |
| `Integrated:LocalPlayerUuid` | empty | The UUID the local player plays with, e.g. `0f3c2b1a-1111-2222-3333-444455556666`. |
| `Integrated:JoinSecret` | none | The per-launch join secret. Without one, nobody can join as the local player. |
| `Port` | `25565` | The loopback listener's port; `0` lets the operating system pick one, reported in the `ready` event. |
| `BindAddress` | `127.0.0.1` (dedicated: every IPv4 address) | The listener's IP address. |
| `OnlineMode` | `true` | Whether other players need Mojang authentication after Open to LAN. The local player never does. |
| `MaxPlayers` | `8` (dedicated: `25`) | |
| `AllowLan` | `false` (dedicated: `true`) | The dedicated server's own LAN advertising; Open to LAN advertises the world itself. |
| `Network:ConnectionThrottle` | `0` (dedicated: `15000`) | Milliseconds before an address may connect again. |
| `ViewDistance`, `SimulationDistance` | `10` | In chunks. |
| `PregenerateChunkRange` | `15` | Chunks generated around the spawn in every direction, in every dimension, when the world is created. Lower values create worlds faster. |
| `NewWorld:*` | | How a new world is created (see [World](#world)). |

The integrated defaults are the lowest-priority configuration source, so any setting overrides them. Standard input
is read without the interactive prompt, commands typed there aren't echoed to the log, and the server stops when
standard input ends, so a client that crashes doesn't leave the server running.

## Control channel

Standard output carries control events; logs go to the log file and to standard error as plain lines without colors
(an exception adds its stack trace on the following lines). Whatever else writes to the console is redirected to
standard error too.

Every control line, in both directions, is `@obsidian:` followed by one compact camelCase JSON object, then a line
feed. Other lines on standard input are ordinary console commands; other lines on standard output can be ignored.

### Commands (standard input)

| Command | Effect |
| --- | --- |
| `{"command":"pause"}` | Stops ticking the worlds (connections and keep-alives keep running). On the change to paused it saves everything once, like vanilla's "Saving and pausing game...", with `saving`/`saved` events. Then `paused`. |
| `{"command":"resume"}` | Ticks the worlds again. Then `resumed`. |
| `{"command":"save","flush":true}` | Saves everything: online players, the world and its dimensions (regions, level.dat, maps) and the user cache. `saving`, then `saved` or `saveFailed`. Every save is flushed to disk, so `flush` is accepted and ignored. |
| `{"command":"stop"}` | Stops gracefully: `stopping`, then disconnects the players, saves everything, releases the world and exits with code 0. The end of standard input does the same. |
| `{"command":"publish","port":25565,"gameMode":"survival","allowCommands":true}` | Open to LAN, below. `port` 0 or missing picks a free port; `gameMode` missing keeps the world's default. Then `published` or `publishFailed`. |

Malformed control lines and unknown commands are logged and ignored. Commands run one at a time in arrival order.
Once the server is stopping, further control commands are logged and ignored, and the final save waits for a save
that's still running.

Open to LAN works like vanilla's `IntegratedServer.publishServer`: the server also listens on every address at the
port (the loopback listener keeps working), advertises `<local player name> - <level name>` to the LAN (multicast
224.0.2.60:4445, every 1.5 seconds), and from then on lets other players join. Players who join afterwards get
`gameMode` (vanilla's forced game type; not in hardcore worlds). With `allowCommands`, every player may use commands
from then on, and online players get their new permission level and command tree. Operators granted in-game (`/op`) are
kept in memory only. The `/publish [<allowCommands>] [<gameMode>] [<port>]` command (operators only, integrated
servers only) runs the same thing and also emits the event.

### Events (standard output)

| Event | When |
| --- | --- |
| `{"event":"progress","stage":"preparing","percent":0}` | While the world loads, at most four per second. `stage` is `preparing` for an existing world and `generating` while a new one generates (all three dimensions in turn); `percent` is 0 to 100. |
| `{"event":"ready","port":51234}` | The world is ready and the loopback listener listens on `port`. |
| `{"event":"saving","autosave":true}` / `{"event":"saved","autosave":true}` | Around every save: autosaves every 6000 ticks (vanilla's interval; paused ticks don't count), and the saves of `pause` and `save` with `autosave` false. |
| `{"event":"saveFailed","message":"..."}` | A save failed; it's in the log too. |
| `{"event":"stats","tickMs":3.2}` | About once a second while not paused: the mean time the last 100 world ticks took to run, in milliseconds (vanilla's smoothed tick time). |
| `{"event":"paused"}` / `{"event":"resumed"}` | Replies to `pause` and `resume`. |
| `{"event":"published","port":25565}` / `{"event":"publishFailed","message":"..."}` | Open to LAN's outcome, from the control command or `/publish`. |
| `{"event":"stopping"}` | Once, when a graceful stop starts, whatever started it. |
| `{"event":"crashed","message":"..."}` | The server crashed; it stops and exits with code 1. |

## Joining

The local player connects to the loopback port with `Integrated:LocalPlayerName`. After their login hello, the server
sends a login custom query (clientbound `CustomQuery`: transaction id, channel `obsidian:integrated_join`, empty
payload). The client answers with serverbound `CustomQueryAnswer`: the transaction id, `true`, then the UTF-8 bytes of
the join secret as the rest of the packet. The server compares the secret in constant time; a wrong or missing answer,
or none within 10 seconds, disconnects. On success the player logs in with `Integrated:LocalPlayerUuid` and the
configured name, without Mojang authentication or encryption, even in online mode. A second connection as the local
player is refused while they're online.

Until the world is opened to LAN, every other login is refused. Afterwards other players log in normally (Mojang
authentication when `OnlineMode` is true), but nobody else may use the local player's name (names compare without
case).

The local player's permission level is 4 while the world allows commands (`allowCommands` in level.dat) or Open to LAN
allowed them for everyone, and 0 otherwise; other players get 4 only through Open to LAN. Every player receives their
level (vanilla's entity event 24 + level) when they join and whenever it changes.

## World

With `Integrated:WorldPath`, the server loads exactly that folder as a vanilla save and keeps vanilla's
`session.lock` (a snowman, locked exclusively) while it's open; a world that's already open elsewhere fails the start
with a crash event. The folder holds:

- `level.dat` in vanilla 1.21.11's shape (a root compound with `Data`), and its backup `level.dat_old`. Obsidian's
  own `level.dat.old` is read too if it's the only backup.
- `region/` and `entities/` for the overworld, `DIM-1/` for the nether and `DIM1/` for the end with their own `region/`
  and `entities/`, `data/` for maps and `playerdata/`. Obsidian doesn't write `poi/`.

level.dat keeps everything it was loaded with: on each save Obsidian only rewrites the fields it owns (`DataVersion`
4671, `Version`, `GameType`, `hardcore`, `allowCommands`, `Difficulty`, `DifficultyLocked`, `initialized`,
`LastPlayed`, `Time`, `DayTime`, weather, `spawn`, `ServerBrands` with `obsidian` added, and `WasModded`), so vanilla
data Obsidian doesn't model (dragon fight, game rules, scheduled events, `Player` and so on) survives. Obsidian has one
clock: it runs on `DayTime`, and advances `Time` by as much as the clock moved. Chunks are still written with Obsidian's
data version, so vanilla upgrades them when it opens the world.

### New worlds

When the folder has no level.dat, the world is created from these settings:

| Key | Default | Meaning |
| --- | --- | --- |
| `NewWorld:LevelName` | folder name | |
| `NewWorld:Seed` | random | Vanilla's rule: a number is used as-is, other text is hashed with Java's `String.hashCode`, empty is random. |
| `NewWorld:WorldType` | `default` | `default` (vanilla-parity generators for all three dimensions) or `flat` (Obsidian's superflat overworld, vanilla nether and end). `large_biomes`, `amplified` and `single_biome_surface` aren't supported yet and crash the start with a message saying so. |
| `NewWorld:FlatPreset` | | Ignored: Obsidian's superflat generator has fixed layers (bedrock, 3 dirt, grass, plains), which level.dat describes. |
| `NewWorld:GenerateStructures` | `true` | Stored (`generate_features`); generation doesn't read it yet. |
| `NewWorld:BonusChest` | `false` | Stored (`bonus_chest`); Obsidian doesn't place the chest. |
| `NewWorld:GameMode` | `survival` | `survival`, `creative`, `adventure` or `spectator`; new players start in it. |
| `NewWorld:Difficulty` | `normal` | `peaceful`, `easy`, `normal` or `hard`; stored. |
| `NewWorld:Hardcore` | `false` | Stored, and sent to players (hardcore hearts). |
| `NewWorld:AllowCommands` | `false` | Whether the local player may use commands. |
| `NewWorld:GameRules:<rule>` | | Vanilla rule ids without the namespace (configuration keys can't contain colons), e.g. `--NewWorld:GameRules:keep_inventory=true`. Stored in `game_rules` as `minecraft:<rule>`: `true`/`false` as bytes, integers as ints; other values are skipped. Obsidian doesn't implement game rules yet, so none are applied. |

A stop (or the end of standard input) while a new world generates ends the generation within moments, and the server
stops gracefully as usual. That world gets no level.dat, so it isn't a world yet (vanilla lists only folders with one):
opening the folder again creates the world from the `NewWorld` settings and keeps the chunks generated so far, so open
it with the same seed or delete the folder.

An existing world's overworld generator comes from its `WorldGenSettings`: vanilla's default noise overworld and flat
worlds load; other presets (large biomes, amplified, single biome, custom) fail the start with a message.
