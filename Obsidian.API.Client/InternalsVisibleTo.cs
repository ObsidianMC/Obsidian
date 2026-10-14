using System.Runtime.CompilerServices;

// The client implements this API: it attaches screens to itself and raises widgets' events through internal members
// mods can't call.
[assembly: InternalsVisibleTo("Obsidian.ClientApp")]
[assembly: InternalsVisibleTo("Obsidian.ClientApp.Tests")]
