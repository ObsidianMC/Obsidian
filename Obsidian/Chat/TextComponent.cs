using Newtonsoft.Json;

namespace Obsidian.Chat
{
    public class TextComponent
    {
        [JsonProperty("action")]
        public TextAction Action;

        [JsonProperty("value")]
        public string Value;

        [JsonProperty("translate", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Translate;
    }

    public enum TextAction
    {
        /// <summary>
        /// Opens the given URL in the default web browser. Ignored if the player has opted to disable links in chat;
        /// may open a GUI prompting the user if the setting for that is enabled. The link's protocol must be set and must be http or https, for security reasons.
        ///
        /// Only used for click event.
        /// </summary>
        open_url,

        /// <summary>
        /// Runs the given command. Not required to be a command - clicking this only causes the client to send the given content as a chat message, so if not prefixed with /, they will say the given text instead.
        /// If used in a book GUI, the GUI is closed after clicking.
        ///
        /// Only used for click event.
        /// </summary>
        run_command,

        /// <summary>
        /// Only usable for messages in chat. Replaces the content of the chat box with the given text - usually a command, but it is not required to be a command (commands should be prefixed with /).
        ///
        /// Only used for click event.
        /// </summary>
        suggest_command,

        /// <summary>
        /// Only usable within written books. Changes the page of the book to the given page, starting at 1. For instance, "value":1 switches the book to the first page.
        /// If the page is less than one or beyond the number of pages in the book, the event is ignored.
        ///
        /// Only used for click event.
        /// </summary>
        change_page,

        /// <summary>
        /// The text to display. Can either be a string directly ("value":"la") or a full component ("value":{"text":"la","color":"red"}).
        ///
        /// Only used for hover event.
        /// </summary>
        show_text,

        /// <summary>
        /// The NBT of the item to display, in the JSON-NBT format (as would be used in /give).
        /// Note that this is a String and not a JSON object - it should either be set in a String directly ("value":"{id:35,Damage:5,Count:2,tag:{display:{Name:Testing}}}") or as text of a component ("value":{"text":"{id:35,Damage:5,Count:2,tag:{display:{Name:Testing}}}"}).
        /// If the item is invalid, "Invalid Item!" will be drawn in red instead.
        ///
        /// Only used for hover event.
        /// </summary>
        show_item,

        /// <summary>
        /// A JSON-NBT String describing the entity. Contains 3 values: id, the entity's UUID (with dashes); type (optional), which contains the resource location for the entity's type (eg minecraft:zombie); and name, which contains the entity's custom name (if present).
        /// Note that this is a String and not a JSON object. It should be set in a String directly ("value":"{id:7e4a61cc-83fa-4441-a299-bf69786e610a,type:minecraft:zombie,name:Zombie}") or as the content of a component. If the entity is invalid, "Invalid Entity!" will be displayed.
        /// Note that the client does not need to have the given entity loaded.
        ///
        /// Only used for hover event.
        /// </summary>
        show_entity
    }
}