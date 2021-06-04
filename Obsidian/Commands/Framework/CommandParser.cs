using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.Commands.Framework
{
    public class CommandParser
    {
        public string Prefix { get; }

        public CommandParser(string prefix)
        {
            Prefix = prefix;
        }

        public bool IsCommandQualified(string input, out ReadOnlyMemory<char> qualifiedCommand)
        {
            if (input.StartsWith(Prefix))
            {
                qualifiedCommand = input.AsMemory(Prefix.Length);
                return true;
            }

            qualifiedCommand = null;
            return false;
        }

        public static string[] SplitQualifiedString(ReadOnlyMemory<char> qualifiedString)
        {
            ReadOnlySpan<char> input = qualifiedString.Span;
            var tokens = new List<string>();

            var buffer = new StringBuilder();
            bool inQuote = false;
            bool escape = false;

            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == ' ' && !inQuote)
                {
                    // flush buffer
                    tokens.Add(buffer.ToString());
                    buffer.Clear();
                }
                else if (escape)
                {
                    // escape table
                    switch (input[i])
                    {
                        default: // any escaped char will be pushed back into the buffer.
                            buffer.Append(input[i]);
                            break;
                        case 'n':
                            buffer.Append('\n');
                            break;
                        case 'r':
                            buffer.Append('\r');
                            break;
                        case 't':
                            buffer.Append('\t');
                            break;
                        case '0':
                            buffer.Append('\0');
                            break;
                        case 'b':
                            buffer.Append('\b');
                            break;
                    }
                    escape = false;
                }
                else if (input[i] == '\\')
                {
                    escape = true;
                }
                else if (input[i] == '"')
                {
                    // toggle quotes
                    inQuote = !inQuote;
                }
                else
                {
                    // else, next token
                    buffer.Append(input[i]);
                }
            }

            if (buffer.Length > 0)
            {
                // clear remaining buffer
                tokens.Add(buffer.ToString());
            }

            return tokens.ToArray();
        }
    }
}
