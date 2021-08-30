using Obsidian.API;
using Obsidian.API.Performance;
using System.Globalization;
using Xunit;

namespace Obsidian.Tests
{
    public class FormattableChatMessageTests
    {
        public FormattableChatMessageTests()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }

        [Fact]
        public void FormatsBooleansCorrectly()
        {
            string format = "Is it {0}? Yes it's {0,10}";
            bool arg = true;

            var formattableMessage = new FormattableChatMessage<bool>(format);
            Utf8Message actual = formattableMessage.Format(arg);
            string actualString = actual.ToString();

            var chatMessage = ChatMessage.Simple(string.Format(format, arg));
            Utf8Message expected = chatMessage.ToUtf8Message();
            string expectedString = expected.ToString();

            Assert.Equal(expectedString, actualString);
        }

        [Fact]
        public void FormatsIntegersCorrectly()
        {
            string format = "Is it {0 , -10 :n}? Yes it's {0,10:n}. Or was it {0}?";
            int arg = -32_767;

            var formattableMessage = new FormattableChatMessage<int>(format);
            Utf8Message actual = formattableMessage.Format(arg);
            string actualString = actual.ToString();

            var chatMessage = ChatMessage.Simple(string.Format(format, arg));
            Utf8Message expected = chatMessage.ToUtf8Message();
            string expectedString = expected.ToString();

            Assert.Equal(expectedString, actualString);
        }

        [Fact]
        public void FormatsFloatsCorrectly()
        {
            string format = "Is it {0}? Yes it's {0,10}";
            float arg = -123.456f;

            var formattableMessage = new FormattableChatMessage<float>(format);
            Utf8Message actual = formattableMessage.Format(arg);
            string actualString = actual.ToString();

            var chatMessage = ChatMessage.Simple(string.Format(format, arg));
            Utf8Message expected = chatMessage.ToUtf8Message();
            string expectedString = expected.ToString();

            Assert.Equal(expectedString, actualString);
        }
    }
}
