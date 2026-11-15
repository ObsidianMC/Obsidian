# Avatar profile control-pick

**Status:** Block and entity item picking is implemented for 26.3. Vanilla also prints an avatar's profile when a
creative player with permission to use game-master blocks control-picks an avatar or player. That diagnostic path
uses `FetchProfileCommand.printForAvatar`; Obsidian has no corresponding `FetchProfileCommand` or profile diagnostic
command, so this side effect is not implemented. The omission does not change entity item selection.
