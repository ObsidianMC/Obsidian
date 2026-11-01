# Item components without a saved codec


**Status:** open.

A stack saves every modeled persistent vanilla component and plugins' components exactly, but some vanilla
components are only modeled for the network (`OpaqueDataComponent`; the list is in
[plugin content](../plugin-content.md#item-components)). Saving a stack leaves such a component out rather than
failing the whole chunk or player save, so it's lost across a restart: a mob-variant bucket or a `tooltip_display`
a creative player set, say.

**Where to start:** model each type's persistent codec from vanilla's `DataComponents` registration, checked
against the jar's codecs with `ComponentFixtures`, as the 63 modeled types are.
