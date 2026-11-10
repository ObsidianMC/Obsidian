# Block-use state validation

Known-gaps wording for PR #701: item exchanges revalidate their destinations, but the branch does not establish
that every block use commits against the state it originally read. These paths still use state captured when the
click arrived, or resolve a menu after awaited work without a complete destination identity check:

- Note block tuning.
- Bed use.
- Non-charging respawn anchor use.
- Redstone component use, including its preliminary button-state check.
- Tying leads to fences.
- General menu opening and container resolution.

Keep these outside the transaction follow-up. They need their own state validation and vanilla captures where
the observable behavior changes.

## Programmatic player uses

`UseItemOnPacket` accepts only the engine's concrete `Player` in an `AbstractLevel`. Vanilla uses, placement
and exchanges additionally require that same player object to remain connected and registered in the level,
alive and outside respawning. An unregistered or departed actor is refused; a custom `IClientPlayer` is ignored
before dispatch or acknowledgement. The packet handler has no public refusal result explaining these cases.
The concrete-`Player` entry guard and the membership requirement were introduced by PR #701; 26.3.x accepted
`IClientPlayer` at entry, although some individual uses already needed engine player types.

This packet is not a general programmatic placement API. Plugins should use their own interaction logic or the
level's world-write APIs, such as `ILevel.SetBlockAsync`, which do not require a connected actor. Plugin block
callbacks can run before the vanilla lifecycle check, so an unregistered actor is not a supported way to invoke
those callbacks either.
