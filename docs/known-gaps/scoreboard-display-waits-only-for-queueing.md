# Scoreboard display waits only for queueing


**Status:** decided. Documented on `IPlayer.DisplayScoreboardAsync`.

`IPlayer.DisplayScoreboardAsync` returns once the scoreboard's packets are queued in the packet broadcaster, not once
they're sent. Displays, updates and removals stay in order with each other, since they're all queued under the
scoreboard's lock. A packet a plugin sends the player directly afterwards may still arrive first.

Waiting for delivery was tried and reverted: plugins' packet handlers run in the player's outgoing pipeline as it
sends, so a handler that displays a scoreboard would wait on the pipeline it runs in.

**Where to start, if it's ever needed:** a delivery wait that packet handlers can't reach, such as one that refuses
to run inside the pipeline.
