﻿namespace Obsidian.API;

public enum MessageType : int
{
    Chat,
    System,
    ActionBar,
    SayCommand,
    MessageCommand,
    TeamMessageCommand,
    EmoteCommand,
    TellRawCommand
}
