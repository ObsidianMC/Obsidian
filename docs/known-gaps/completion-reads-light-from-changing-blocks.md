# Completion reads light from changing blocks

**Status:** known world generation parity gap; retained approximation in the live 26.3 switch.

Vanilla finishes LIGHT before FULL. `LevelChunk.postProcessGeneration` reads that stored light for mushroom
survival; its block changes enqueue light updates without immediately changing the light survival reads.
Obsidian completes blocks before its generation lighting pass. `CompletionLight` reconstructs the light-13
survival threshold from the current blocks, so an earlier fluid tick in the same completion pass can change
the brightness a later mushroom sees.

For example, in a no-skylight dimension with aquifers enabled, a marked lava source three air steps from a
supported mushroom gives stored block light 12. If completion processes the lava first and creates flowing lava
two steps away, Obsidian sees light 13 and removes the mushroom. Vanilla still reads 12 and keeps it until any
later gameplay update. This can change final generated blocks in such dimensions.

The completion fixture records vanilla's raw mushroom light before and after FULL and checks the survival
threshold for its captured cells, alongside final blocks and exact scheduled ticks. Those two natural chunks
do not exercise a completion change that crosses the mushroom light threshold; the replay does not establish
parity for that timing case.

Fix by scheduling generation lighting before completion, reading the stored light during completion, then
relighting changes afterwards. This needs coordinated changes to staged generation, propagation into neighboring
chunks and the benchmark's shared server path; reconstructing light from already changed blocks cannot fix it.
