using BenchmarkDotNet.Attributes;
using Obsidian.API;
using Obsidian.API.Performance;
using System.Collections.Generic;

namespace Benchmark
{
    public class Benchmarks
    {
        public FormattableChatMessage<int, int, int, int, int> FormattableChatMessage { get; set; }

        public IEnumerable<object> Args => new object[][] { new object[] { -123, 0, 123, 456, 789 } };

        public Benchmarks()
        {
            FormattableChatMessage = new FormattableChatMessage<int, int, int, int, int>("START {0} {1} {2} {3} {4} END");
        }

        [Benchmark, ArgumentsSource(nameof(Args))]
        public Utf8Message Formattable(int value1, int value2, int value3, int value4, int value5)
        {
            return FormattableChatMessage.Format(value1, value2, value3, value4, value5);
        }

        [Benchmark(Baseline = true), ArgumentsSource(nameof(Args))]
        public Utf8Message Normal(int value1, int value2, int value3, int value4, int value5)
        {
            return ChatMessage.Simple($"START {value1.ToString()} {value2.ToString()} {value3.ToString()} {value4.ToString()} {value5.ToString()} END").ToUtf8Message();
        }
    }
}
