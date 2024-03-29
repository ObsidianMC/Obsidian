﻿namespace Obsidian.API.Registry.Codecs.Chat;

public sealed record class ChatDecoration
{
    public List<string>? Parameters { get; set; }

    public string? TranslationKey { get; set; }

    public object? Style { get; set; }
}
