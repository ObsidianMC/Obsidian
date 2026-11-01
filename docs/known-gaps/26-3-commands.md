# 26.3 commands


**Status:** Phase 6's command additions and server defaults are implemented, with the limits below.

`/swing`, clock-based `/time`, `/compute`, `/posteffect`, `/unpublish`, slot sources in `/item` and
`/execute if|unless items|slots`, and inline `/place feature` have command modules. `/data modify … compute`
evaluates integer and float providers into NBT; storage uses vanilla's per-namespace `command_storage.dat` envelope.
Commands expose success separately from their integer result, so a successful zero and an arithmetic failure remain
distinct through `/execute run`. Class-level command permissions apply to commands outside groups too.

The generated `commands-26.3.json` supplies vanilla's client grammar, argument bytes, suggestion identifiers,
restriction flags and redirects. It describes the complete vanilla trees, including older branches the existing
command implementations still lack. Server completion handles the new literals, clock markers, timelines, provider
identifiers and slot names. `CommandFixtures.java` captures the grammar and execution references;
`VanillaCommands26` replays them, including nested container contents, bundles and charged projectiles growing
within their limits, and distribution across targets.

- Number providers share the data-system evaluator. Score and storage inputs work; numeric environment-attribute
  sampling fails explicitly until the environment runtime supplies a general numeric sampler. Predicates inherit
  the shared loot evaluator's limitations.
- `/data modify … compute` supports compound names and individual list indices, plus numeric set/insert/prepend/
  append. Wildcard and compound-match paths are unsupported. Other modify sources and general compound merging
  remain outside the existing `/data` implementation.
- Slot sources support literal ranges, inline groups/ranges, limits, item/count filters and contents. Players'
  inventories/equipment, mobs' equipment and block containers are exposed. Vehicle inventories, registered slot
  sources/modifiers, inline item component patches and component-rich item predicates remain unsupported.
- Inline features use the 26.3 feature registry and the live feature view. Placement requires its surrounding
  chunks to be loaded; unsupported feature types fail explicitly. Feature entity spawning in a live level is not
  implemented. The other `/place` modes remain unsupported.
- `/execute` retains its earlier supported context operations and conditions; storage callbacks (`store`), functions
  and the rest of vanilla's older execution branches are still absent.

Whitelist defaults to on for fresh installs, whose generated `server.json` sets it explicitly; an existing
`server.json` without a `whitelist` entry keeps it off and gets a startup notice. An empty optional IP whitelist no
longer rejects every connection before player login;
integrated worlds use their existing owner/LAN admission policy. Chat and commands have independent ten-second
spam budgets, with vanilla's 20-tick charge, one-tick recovery, boundary and disabled-limit behavior. Operators and
the integrated owner are exempt from disconnection.
