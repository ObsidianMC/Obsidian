using System.Runtime.CompilerServices;

// The client implements this API: it attaches its world view and screens to the API's objects, and raises widgets'
// events, through internal members mods can't call.
[assembly: InternalsVisibleTo("Obsidian.ClientApp")]
[assembly: InternalsVisibleTo("Obsidian.ClientApp.Tests")]
