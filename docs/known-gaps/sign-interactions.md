# Sign interactions

**Status:** open.

Sign item application and main-hand editing have separate use hooks. These inherited gaps remain:

- Editor ownership has no side or expiry. Another player can take over the reservation or apply an item while
  someone else edits the sign.
- Opening the editor does not reject rich or otherwise non-editable text.
- Sign click commands are not executed.
- Hanging signs do not implement vanilla's pass-through when another hanging sign is used on them.

These gaps are separate from the block-use dispatcher and transaction changes in PR #701.

**Where to start:** capture editor ownership, text editability, click commands and hanging-sign pass-through in
`block_use`, then extend `Signs` and its use hooks to match those cases.
