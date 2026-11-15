# Removed components in item equality

**Status:** `ItemStack.Equals` compares an item's holder and its stored components, but not the components the stack
removes from its item's defaults. Vanilla's `isSameItemSameComponents` compares the whole patch, removals included.
Two stacks that differ only because one removes a default component therefore compare equal in Obsidian and unequal
in vanilla.

Pick-block resolves the outer stack's defaults and removals before comparing (`HasSameEffectiveComponents`), so the
gap only shows for item stacks nested in a component, such as a picked chest's contents. A nested stack that removes a
default component is matched as if it kept it. Vanilla compares nested stacks by their patch, so the fix is for
`Equals` to compare removals too. That changes equality everywhere items are compared (stacking, merging, equipment
checks), so it needs its own change and tests.
