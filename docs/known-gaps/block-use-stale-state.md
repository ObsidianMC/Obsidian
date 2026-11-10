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
