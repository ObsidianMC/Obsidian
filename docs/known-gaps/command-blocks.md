# Command blocks

**Status:** open. Impulse, repeating and chain command blocks run their commands like vanilla's `CommandBlock`
(`CommandBlocks` and `CommandBlockEntity`, in `Obsidian/WorldData`). They run on a rising redstone edge, every tick
while powered or always active, and down chains through `facing`. Conditional blocks check the block behind them,
and the `max_command_sequence_length`, `command_block_output`, `send_command_feedback` and `command_blocks_work` rules
apply. Comparators read `SuccessCount`. Operators in creative mode edit them through the `SetCommandBlock` packet and
are sent the block's data when they open it, and players are told their permission level on joining, respawning,
changing level and having it changed. Command blocks and command block minecarts share one executor,
`StoredCommand`, which runs commands at the game master level (2) and saves vanilla's fields.
`command_blocks.json` (`VanillaCommandBlocks`) captures it all tick by tick, with every case matching from the start;
see [scenario captures](../scenario-captures.md). What follows is what still differs.

26.3 has no `enable-command-block` server property: vanilla's `isCommandBlockEnabled` is the `command_blocks_work`
game rule, which is what Obsidian honours.

## Running

- **When a run happens.** A due tick runs the block's command on the level's entity path, after the tick's block and
  fluid ticks, where a command can run; vanilla runs it among the block ticks. Runs keep their order, and everything a
  run changes is in place by the end of the same game tick. But a block tick due in the same game tick that reads what
  a command changed sees it before the command ran.
- **Output and operators.** Vanilla tells each message apart as a success (kept with `send_command_feedback`, and told
  to operators with `command_block_output`) or a failure (always kept). Obsidian's command sender can't, so every
  message of a command that succeeded counts as a success, and every message of one that failed as a failure.
  Of the successes vanilla sends without telling operators (`sendSuccess(message, false)`), only `/execute`'s
  "Test passed" is kept from them (`CommandFeedback.UnbroadcastSuccessAsync`); other commands' are told here.
  `log_admin_commands` doesn't write command block successes to the server log.
- **Selectors with a position or distance.** Vanilla's `@a`, `@p` and `@r` go through the server's player list,
  whose order Obsidian keeps (joining appends a player, respawning after death or the End moves them to its end).
  But with `x`, `y`, `z`, `distance`, `dx`, `dy` or `dz` vanilla goes through the level's own player list instead,
  in the order players arrived in that level, and only that level's players. Obsidian still uses the server's list
  there, so `@a[distance=..5,limit=1]` and equal distances can pick another player.
- **Colours and chat types.** Obsidian's failures carry their red as a `§c` code in the text, and are saved that way
  in `LastOutput`, where vanilla saves a style. A command block's `/say` reaches players as system chat reading
  `[@] text`, where vanilla sends disguised chat of the announcement type; players see the same line.
- **Minecart names.** A command block minecart's commands go by `@` in operators' messages, where vanilla's go by
  the cart's name ("Minecart with Command Block").
- **A throwing command** is logged; vanilla crashes the server with "Executing command block".

## Concurrency

A run's command executes outside the level's tick lock, as commands await. Edits (the `SetCommandBlock` packet) and
redstone change the block under the lock, so an edit can land while the block's command is still running, which
vanilla can't do: it handles edits between ticks. The run then commits its results (`SuccessCount`, `LastOutput`,
`LastExecution`) under the lock only while no edit set the command since it started (`StoredCommand.Revision`) and its
block entity is still the one at its position, so an edit keeps its reset. What the command itself did (blocks set,
chat, operators told) stays done. The block saves from any thread, like the server's other block entities, so a save
racing a commit may take some fields from before it and some from after; the run's own change is saved again at the
end of the tick.

Commands run inside the level's tick, so they mustn't wait for the gates: `/save` from a command block or minecart
saves the world of the level it runs in from the server's background work, once the ticks let go of the gates (see
`Server.RunOutsideTicksAsync`). They hold no inventory, transfer or chunk lock, so a command that waits for an
inventory isn't waiting under one.

## Items and editing

- **Placed items' data.** A placed command block whose item carries `block_entity_data` keeps its defaults, as
  vanilla's `setPlacedBy` leaves them. But Obsidian doesn't load that data into the block entity.
  `collectImplicitComponents` (the block's custom name onto a picked or broken item) and `removeComponentsFromTag`
  aren't modelled either.
- **Edit updates.** Vanilla's `onUpdated` sends the block's state to clients after every edit, even an unchanged
  one. Obsidian sends it only when the block changed. Clients show the same.

**Where to start:** `CommandBlocks.TickAsync` and `StoredCommand.PerformAsync`. A per-message success flag on
`ICommandSender` would fix the output rules.
