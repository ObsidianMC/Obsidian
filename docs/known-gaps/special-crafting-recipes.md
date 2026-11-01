# Special crafting recipes


**Status:** open. The player's grid, crafting table and public `IServer.EvaluateCrafting` share one matcher, for
shaped and shapeless recipes and 26.1's configurable `crafting_dye`, `crafting_imbue` and `crafting_transmute`
(`SpecialCrafting`). The older special recipes (`crafting_special_*` and `crafting_decorated_pot`), such as repairing
tools and decorating shields, are not evaluated yet; their recipe-specific outputs and remainders still need
implementation. A dye, imbue or transmute result whose component patch sets a component Obsidian can't read from a
saved stack (see [item components without a saved codec](item-components-without-a-saved-codec.md)) fails to load
rather than crafting without it. Public evaluations are detached plans, not inventory reservations or result-slot commit callbacks for
custom menus.

**Where to start:** capture each supported special recipe's output and remainder behavior in vanilla 26.3 and
extend the shared matcher and consumption path together. Add a custom-menu commit API when a concrete menu needs it.
