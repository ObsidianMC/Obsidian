using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.Commands.Framework
{
    public class CommandParser
    {
        private string _prefix;

        public CommandParser(string prefix)
        {
            _prefix = prefix;
        }

        public bool IsCommandQualified(ReadOnlyMemory<char> input, out ReadOnlyMemory<char> qualifiedCommand)
        {
            qualifiedCommand = null;
            if (input.Span.StartsWith(_prefix))
            {
                qualifiedCommand = input.Slice(_prefix.Length);
                return true;
            }

            return false;
        }

        public string[] SplitQualifiedString(ReadOnlySpan<char> input)
        {
            List<string> tokens = new List<string>();

            StringBuilder buffer = new StringBuilder();
            bool inquote = false;
            bool escape = false;

            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == ' ' && !inquote)
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
                    inquote = !inquote;
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
