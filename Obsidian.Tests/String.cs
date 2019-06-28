using Obsidian.Net;

using System;
using System.Collections.Generic;

using Xunit;

namespace Obsidian.Tests
{
    public class String
    {
        [MemberData(nameof(StringData))]
        [Theory(DisplayName = "Equal strings", Timeout = 100)]
        public async void SameAsync(string expectedValue)
        {
            using var stream = new MinecraftStream();

            await stream.WriteStringAsync(expectedValue);

            stream.Position = 0; //Reset to beginning for read

            string actualValue = await stream.ReadStringAsync();

            Assert.Equal(expectedValue, actualValue);
        }

        public static IEnumerable<object[]> StringData
        {
            get
            {
                var random = new Random();
                var values = new List<object[]>();

                for (int i = 0; i < 5; i++)
                {
                    string value = "";

                    for (int l = 0; l < random.Next(500); l++)
                    {
                        value += (char)random.Next(' ', '~');
                    }

                    values.Add(new[] { value });
                }

                return values;
            }
        }
    }
}