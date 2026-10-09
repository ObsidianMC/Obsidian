# Open protocol decisions

Decisions deferred until the server's protocol work is finished. Each entry says what's open, why it matters and
where to start.

## Validate item components that players send

**Status:** open, to revisit with the protocol work.

`SetCreativeModeSlotPacket` reads the player's stack with `ReadUntrustedItemStack`. That checks the stack's framing:
lengths, nesting and that each component's value consumes exactly its bytes. It doesn't check what the values refer
to. So a creative-mode player can send an item with, for example, an enchantment id outside the enchantment
registry. The server keeps it and sends it on (equipment, container contents), and vanilla clients that decode it
disconnect, since vanilla resolves registry ids when it reads a component.

This predates the component codec work: before it, the server didn't read creative stacks' component values at all.

**Where to start:**

- Validate registry-backed values in untrusted components before accepting a stack: holder ids (enchantments,
  potions, trims, instruments, jukebox songs, painting variants, banner patterns and so on), plus value ranges
  vanilla's codecs constrain. Reject the packet, as vanilla does, rather than storing the stack.
- Keep trusted reads (Obsidian's own data) permissive, so preserved opaque components still round-trip.
- Apply the same checks to any other serverbound packet that carries full stacks. (Container clicks only send hashed
  stacks, which the server compares rather than stores.)
