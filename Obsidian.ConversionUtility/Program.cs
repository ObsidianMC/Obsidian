using CommandLine;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace Obsidian.ConversionUtility;

internal class Program
{
    public class Options
    {
        [Option('m', "mode", Required = true, HelpText = "Which type of file or data to convert")]
        public Mode Mode { get; set; }

        [Option('i', "input", Required = true, HelpText = "File or directory to convert")]
        public string Path { get; set; }

        [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages.")]
        public bool Verbose { get; set; }
    }

    public enum Mode
    {
        ServerProperties
    }

    private static void Main(string[] args)
    {
        Parser.Default.ParseArguments<Options>(args).WithParsed(o =>
        {
            switch (o.Mode)
            {
                case Mode.ServerProperties:
                    string[] lines = File.ReadAllLines(o.Path);

                    var values = new Dictionary<string, string>();

                    foreach (string line in lines)
                    {
                        if (!line.Contains('='))
                        {
                            continue;
                        }

                        var split = line.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);

                        if (split.Length != 2)
                        {
                            continue;
                        }

                        values[split[0]] = split[1];
                    } //Convert properties file to C#

                        var json = JsonConvert.SerializeObject(new
                    {
                        motd = values["motd"],
                        port = values["server-port"],
                        onlineMode = values["online-mode"]
                    });

                    File.WriteAllText("config.json", json);

                    Console.WriteLine("Done");

                    break;

                default: Console.WriteLine($"Unknown mode ({o.Mode})"); break;
            }
        });
    }
}
