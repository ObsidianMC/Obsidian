using Obsidian.API;
using Obsidian.API.Builders;
using Obsidian.API.Registries;
using Obsidian.API.Registry.Codecs.Dialogs;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Nbt;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class Dialogs
{
    [Fact]
    public async Task FeedbackContentAndEndpointStayUsable()
    {
        {
            const string details = "### Reporting player\nUsername: Steve\nUUID: 00000000-0000-0000-0000-000000000001";
            using var content = Server.CreateFeedbackContent("**Test** @everyone\nSecond line", details);
            using var json = JsonDocument.Parse(await content.ReadAsStringAsync());
            var root = json.RootElement;
            Assert.Equal(1 << 15, root.GetProperty("flags").GetInt32());
            Assert.False(root.TryGetProperty("content", out _));
            Assert.False(root.TryGetProperty("embeds", out _));
            Assert.Equal(0, root.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength());
            var container = Assert.Single(root.GetProperty("components").EnumerateArray());
            Assert.Equal(17, container.GetProperty("type").GetInt32());
            var components = container.GetProperty("components");
            Assert.Equal(10, components[1].GetProperty("type").GetInt32());
            Assert.Contains(
                "\\*\\*Test\\*\\* @everyone\nSecond line", components[1].GetProperty("content").GetString());
            Assert.Equal(14, components[2].GetProperty("type").GetInt32());
            Assert.Equal(details, components[3].GetProperty("content").GetString());
        }

        {
            using var content = Server.CreateFeedbackContent(new string('*', 1500), new string('x', 4000));
            using var json = JsonDocument.Parse(await content.ReadAsStringAsync());
            var text = json.RootElement.GetProperty("components")[0].GetProperty("components").EnumerateArray()
                .Where(x => x.GetProperty("type").GetInt32() == 10)
                .Select(x => x.GetProperty("content").GetString());
            Assert.True(text.Sum(x => x!.Length) <= 4000);
        }

        {
            var endpoint = new Uri(
                "https://discord.com/api/webhooks/123/token?thread_id=456&with_components=false&wait=false");
            Assert.True(Server.TryGetFeedbackWebhook(endpoint.AbsoluteUri, out _));
            Assert.False(Server.TryGetFeedbackWebhook("http://discord.com/api/webhooks/123/token", out _));
            var url = Server.GetFeedbackComponentWebhook(endpoint);
            Assert.Contains("thread_id=456", url.Query);
            Assert.Contains("with_components=true", url.Query);
            Assert.Contains("wait=true", url.Query);
            Assert.DoesNotContain("with_components=false", url.Query);
            Assert.DoesNotContain("wait=false", url.Query);
        }
    }

    private static NbtCompound Serialize(DialogElement dialog)
    {
        using var buffer = new NetworkBuffer();
        new ShowDialogPacket { Dialog = dialog }.Serialize(buffer);
        using var reader = new NetworkBuffer(buffer.GetBuffer()[..buffer.Size]);
        Assert.Equal(0, reader.ReadVarInt());
        var compound = reader.ReadNbtCompound();
        Assert.Equal(reader.Size, reader.Offset);
        return compound;
    }

    [Fact]
    public void DialogSerializationAndRegistration()
    {
        {
            var dialog = DialogBuilder.Notice(
                "Feedback", DialogAction.Create("Send", DialogClickAction.Submit("obsidian:feedback")))
                .WithMessage("Tell us what you think")
                .WithInput(DialogInput.Text("message", "Feedback", 1500, multiline: true, height: 100))
                .WithPause(false).AfterAction("wait_for_response").Build();
            var root = Serialize(dialog);
            Assert.Equal("minecraft:notice", root.GetString("type"));
            Assert.Equal("wait_for_response", root.GetString("after_action"));
            var input = Assert.IsType<NbtCompound>(Assert.IsType<NbtList>(root["inputs"])[0]);
            Assert.Equal("message", input.GetString("key"));
            Assert.Equal(1500, input.GetInt("max_length"));
            Assert.Equal(100, Assert.IsType<NbtCompound>(input["multiline"]).GetInt("height"));
            var action = Assert.IsType<NbtCompound>(Assert.IsType<NbtCompound>(root["action"])["action"]);
            Assert.Equal("minecraft:dynamic/custom", action.GetString("type"));
            Assert.Equal("obsidian:feedback", action.GetString("id"));
        }

        {
            var button = DialogAction.Create("OK");
            Assert.True(Serialize(DialogBuilder.Confirmation("Confirm", button, button).Build()).HasTag("no"));
            Assert.True(Serialize(DialogBuilder.MultiAction("Menu").WithActions(button).Build()).HasTag("actions"));
            Assert.Equal(2, Serialize(DialogBuilder.ServerLinks("Links").Build()).GetInt("columns"));
            var ids = Assert.IsType<NbtList>(
                Serialize(DialogBuilder.DialogList("Dialogs", "example:first").Build())["dialogs"]);
            Assert.Equal("example:first", Assert.IsType<NbtTag<string>>(ids[0]).Value);
            Assert.Equal(
                "#minecraft:quick_actions",
                Serialize(DialogBuilder.DialogTag("Quick", "minecraft:quick_actions").Build()).GetString("dialogs"));
            foreach (var codec in CodecRegistry.Dialog.All.Values)
                Serialize(codec.Element);
        }

        {
            var root = Serialize(DialogBuilder.Notice("Settings")
                .WithBody(DialogBody.Item("minecraft:stone", description: "Stone"))
                .WithInput(DialogInput.Boolean("enabled", "Enabled", true),
                    DialogInput.SingleOption("choice", "Choice", [new("a"), new("b", "B", true)]),
                    DialogInput.NumberRange("volume", "Volume", 0, 1, 0.5f, 0.1f))
                .Build());
            var inputs = Assert.IsType<NbtList>(root["inputs"]);
            Assert.True(Assert.IsType<NbtCompound>(inputs[0]).GetBool("initial"));
            Assert.Equal(2, Assert.IsType<NbtList>(Assert.IsType<NbtCompound>(inputs[1])["options"]).Count);
            Assert.Equal(0.5f, Assert.IsType<NbtCompound>(inputs[2]).GetFloat("initial"));
            var body = Assert.IsType<NbtList>(root["body"]);
            var itemBody = Assert.IsType<NbtCompound>(body[0]);
            var item = Assert.IsType<NbtCompound>(itemBody["item"]);
            Assert.Equal("minecraft:stone", item.GetString("id"));
        }

        {
            const string id = "dialog_test:settings";
            try
            {
                DialogBuilder.Notice("Settings").Register(id, quickAction: true, pauseScreen: true);
                var registration = Assert.Single(DialogRegistry.GetRegistrations().Where(x => x.Id == id));
                Assert.True(registration.QuickAction);
                Assert.True(registration.PauseScreen);
            }
            finally
            {
                DialogRegistry.Unregister(id);
            }
        }

        {
            const string id = "dialog_test:snapshot";

            try
            {
                var registered = DialogBuilder.Notice("Before").Register(id);

                // The registration shares the title with the dialog it returned; changing it changes nothing sent.
                registered.Title.Text = "After";

                var dialog = Assert.Single(DialogRegistry.GetSerialized(), dialog => dialog.Registration.Id == id);

                var expected = Element(DialogBuilder.Notice("Before").Build().Write);

                Assert.Equal(expected, Element(dialog.ToCodec(0).WriteElement));
            }
            finally
            {
                DialogRegistry.Unregister(id);
            }

            // Writes a registry element as NetworkBuffer.WriteCodec does.
            static byte[] Element(Action<Obsidian.Nbt.Interfaces.INbtWriter> write)
            {
                using var writer = new RawNbtWriter(true);

                write(writer);
                writer.EndCompound();
                writer.TryFinish();

                return writer.Data.ToArray();
            }
        }
    }

    [Fact]
    public void CustomAction_ReadsLengthPrefixedPayloadAndRejectsMalformedLengths()
    {
        using var nbt = new RawNbtWriter(true);
        nbt.WriteString("message", "Hello\nWorld");
        nbt.EndCompound();
        nbt.TryFinish();
        using var buffer = new NetworkBuffer();
        buffer.WriteString("obsidian:feedback");
        buffer.WriteVarInt(nbt.Data.Length);
        buffer.WriteByteArray(nbt.Data);
        var packet = CustomClickActionPacket.Deserialize(buffer.GetBuffer()[..buffer.Size]);
        Assert.Equal("obsidian:feedback", packet.ActionId);
        Assert.Equal("Hello\nWorld", Assert.IsType<NbtCompound>(packet.Payload).GetString("message"));
        Assert.Throws<InvalidDataException>(
            () => CustomClickActionPacket.ValidatePayload([10, 7, 0, 0, 127, 255, 255, 255, 0]));
        Assert.Throws<InvalidDataException>(() => CustomClickActionPacket.ValidatePayload([10]));
        Assert.Throws<InvalidDataException>(() => CustomClickActionPacket.ValidatePayload([0, 0]));
        var nested = Enumerable.Repeat(new byte[]
        {
            10,
            0,
            1,
            (byte)'a'
        }, 34).SelectMany(x => x).ToArray();
        Assert.Throws<InvalidDataException>(
            () => CustomClickActionPacket.ValidatePayload([10, .. nested, .. new byte[35]]));
    }

}
