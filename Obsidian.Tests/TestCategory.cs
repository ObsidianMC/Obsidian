namespace Obsidian.Tests;

/// <summary>
/// Values for <c>[Trait("Category", ...)]</c>, which CI filters tests on.
/// </summary>
internal static class TestCategory
{
    /// <summary>
    /// Tests that generate chunks with the Mojang generator. They're slow, so CI skips them
    /// (<c>--filter Category!=Worldgen</c>). Run them locally when changing world generation.
    /// </summary>
    public const string Worldgen = "Worldgen";
}
