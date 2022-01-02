namespace Obsidian.API;

public enum EClickAction
{
    /// <summary>
    /// Opens the given URL in the default web browser. Ignored if the player has opted to disable links in chat;
    /// may open a GUI prompting the user if the setting for that is enabled. The link's protocol must be set and must be http or https, for security reasons.
    /// </summary>
    OpenUrl,

    /// <summary>
    /// Copies Value to the clipboard.
    /// </summary>
    CopyToClipboard,

    /// <summary>
    /// Runs the given command. Not required to be a command - clicking this only causes the client to send the given content as a chat message, so if not prefixed with /, they will say the given text instead.
    /// If used in a book GUI, the GUI is closed after clicking
    /// </summary>
    RunCommand,

    /// <summary>
    /// Only usable for messages in chat. Replaces the content of the chat box with the given text - usually a command, but it is not required to be a command (commands should be prefixed with /).
    /// </summary>
    SuggestCommand,

    /// <summary>
    /// Only usable within written books. Changes the page of the book to the given page, starting at 1. For instance, "value":1 switches the book to the first page.
    /// If the page is less than one or beyond the number of pages in the book, the event is ignored.
    /// </summary>
    ChangePage
}
