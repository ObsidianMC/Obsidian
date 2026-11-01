# Bubble columns started by water


**Status:** disabled during the 26.3 port and left out of it; see [26.3 gameplay left out of the port](26-3-gameplay-left-out-of-the-port.md).

Since 26.1 soul sand and magma no longer start bubble columns themselves. The water above them does: `LiquidBlock`'s
`onPlace`, `neighborChanged` and `updateShape` (from below) schedule a 20-tick check when the block below is in
`#enables_bubble_column_push_up` or `#enables_bubble_column_drag_down`, and its `tick` updates the column. Obsidian's
soul sand and magma behaviors no longer bind to an owner, so new columns don't form; existing columns still tick. The
two bubble column tests that start a column and send it to the client are skipped; a vanilla capture should
cover the rest of the behavior.

**Where to start:** give water the same hooks (Obsidian's fluid ticks handle water today, so keep its spreading path)
and replace `SoulSandBlock` and `MagmaBlock` in `BlockOwnerBehaviors` with them, then re-enable the skipped tests in
`BubbleColumns`.
