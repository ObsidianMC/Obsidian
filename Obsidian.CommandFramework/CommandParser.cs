using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework
{
    public class CommandParser
    {
        private string _prefix;

        public CommandParser(string prefix)
        {
            this._prefix = prefix;
        }

        public bool IsCommandQualified(string input, out string qualifiedcommand)
        {
            qualifiedcommand = null;
            if(input.StartsWith(_prefix))
            {
                qualifiedcommand = input.Substring(_prefix.Length);
                return true;
            }

            return false;
        }

        public string[] SplitQualifiedString(string input)
        {
            List<string> tokens = new List<string>();

            StringBuilder buffer = new StringBuilder();
            bool inquote = false;
            bool escape = false;

            for(int i = 0; i < input.Length; i++)
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
                    switch(input[i])
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
                }
                else if(input[i] == '\\')
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

            if(buffer.Length > 0)
            {
                // clear remaining buffer
                tokens.Add(buffer.ToString());
            }

            return tokens.ToArray();
        }
    }
}
