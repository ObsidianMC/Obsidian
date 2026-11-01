# Context number provider commands and world sources


**Status:** the shared 26.3 int/float provider evaluator and built-in definitions are available in
`Obsidian.API.Data`. Loot uses it, including arithmetic and conditional providers; captured replays check results
and subsequent random draws. `/compute` and `/data modify … compute` use it with command storage and score
callbacks. Numeric environment sampling remains unsupported in commands; see [command limits](26-3-commands.md).
Provider holder-set tags and non-constant/non-linear enchantment-level values are not implemented.

**Where to start:** use `ContextNumberProviders.ReadInt`/`ReadFloat` (or the string `ParseInt`/`ParseFloat` overloads)
with a per-evaluation `NumberContext`. Supply numeric environment callbacks from the environment runtime and
extend the shared evaluator's remaining provider cases rather than creating a separate arithmetic evaluator.
