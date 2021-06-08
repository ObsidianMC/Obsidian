using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Obsidian.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.Data;
using Obsidian.Logging;
using Obsidian.Services;

namespace Obsidian
{
    public class Program
    {
        private static readonly Dictionary<int, Server> Servers = new();
        private static readonly TaskCompletionSource<bool> cancelKeyPress = new();
        private static bool shutdownPending;

        /// <summary>
        /// Event handler for Windows console events
        /// </summary>
        private static NativeMethods.HandlerRoutine _windowsConsoleEventHandler;

        private const string GlobalConfigFile = "global_config.json";
        private const string DevEnvVar = "DOTNET_ENVIRONMENT";
        private const string ConnectionString = "ServerConnectionStream";

        private static async Task<int> Main()
        {
            Console.Title = $"Obsidian {FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion ?? "1.0.0"}";
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine(asciilogo);
            Console.ResetColor();
            Console.WriteLine($"A C# implementation of the Minecraft server protocol. Targeting: {Server.protocol.GetDescription()}");

            string asmpath = Assembly.GetExecutingAssembly().Location;
            //This will strip just the working path name:
            //C:\Program Files\MyApplication
            string asmdir = Path.GetDirectoryName(asmpath);
            Environment.CurrentDirectory = asmdir;

            var environment = Environment.GetEnvironmentVariable(DevEnvVar) ?? "Production";

            if (!File.Exists(GlobalConfigFile))
            {
                await File.WriteAllTextAsync(Path.Combine(Environment.CurrentDirectory!, GlobalConfigFile), JsonConvert.SerializeObject(new GlobalConfig(), Formatting.Indented));
            }

            try
            {

                var hostBuilder = new HostBuilder()
                .UseEnvironment(environment)
                .ConfigureAppConfiguration((context, builder) =>
                {
                    builder.AddEnvironmentVariables("Obsidian_");

                    builder.AddJsonFile(GlobalConfigFile, true, true);

                    if (context.HostingEnvironment.IsDevelopment())
                    {
                        builder.AddUserSecrets<Program>();
                    }
                })
                .ConfigureLogging((context, builder) =>
                {
                    builder.AddProvider(new LoggerProvider(context.Configuration.GetValue<LogLevel>("logLevel")));
                })
                .ConfigureServices((context, services) =>
                {
                    services.Configure<GlobalConfig>(context.Configuration);

                    var dbKind = context.Configuration.GetValue<DatabaseKind>("databaseKind");
                    var connectionString = context.Configuration.GetConnectionString(ConnectionString);

                    services.AddDbContext<ServerContext>(options =>
                    {
                        switch (dbKind)
                        {
                            case DatabaseKind.SQLite:
                            {
                                options.UseSqlite(connectionString);
                                break;
                            }
                            case DatabaseKind.PostgreSQL:
                            {
                                options.UseNpgsql(connectionString);
                                break;
                            }
                            case DatabaseKind.CockroachDB:
                            {
                                options.UseCockroachDB(connectionString);
                                break;
                            }
                            case DatabaseKind.MySQL:
                            {
                                options.UseMySQL(connectionString);
                                break;
                            }
                            case DatabaseKind.None:
                            {
                                // TODO: Provide some sort of dummy db context that informs consumers none is available.
                                break;
                            }
                            default:
                            {
                                throw new InvalidOperationException("Unexpected value provided for \"databaseKind\".");
                            }
                        }
                    });

                    // Add other services
                    services
                        .AddSingleton<ServerRegistry>()
                        .AddSingleton<ConsoleService>()
                        .AddHostedService<ObsidianServer>();
                }).
                .UseConsoleLifetime();

                using var host = hostBuilder.Build();

                await host.StartAsync();
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Host terminated unexpectedly." + Environment.NewLine + ex);

                if (Debugger.IsAttached && Environment.UserInteractive)
                {
                    Console.WriteLine(Environment.NewLine + "Press any key to exit...");
                    Console.ReadKey(true);
                }

                return ex.HResult;
            }
            finally
            {
                // Flush the logger, if needed
            }
        }

        // TODO: To be removed. Kept for documentation reasons.
        [Obsolete]
        private static async Task MainAsync()
        {
#if RELEASE
            string version = "0.1";
#else
            string version = "0.1-DEV";
            string asmpath = Assembly.GetExecutingAssembly().Location;
            //This will strip just the working path name:
            //C:\Program Files\MyApplication
            string asmdir = Path.GetDirectoryName(asmpath);
            Environment.CurrentDirectory = asmdir;
#endif
            // Kept for consistant number parsing
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Console.Title = $"Obsidian {version}";
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine(asciilogo);
            Console.ResetColor();
            Console.WriteLine($"A C# implementation of the Minecraft server protocol. Targeting: {Server.protocol.GetDescription()}");

            // Hook into Windows' native console closing events, otherwise use .NET's native event.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _windowsConsoleEventHandler += new NativeMethods.HandlerRoutine(OnConsoleEvent);
                NativeMethods.SetConsoleCtrlHandler(_windowsConsoleEventHandler, true);
            }
            else
            {
                Console.CancelKeyPress += OnConsoleCancelKeyPressed;
            }

            if (File.Exists(GlobalConfigFile))
            {
                Globals.Config = JsonConvert.DeserializeObject<GlobalConfig>(File.ReadAllText(GlobalConfigFile));
            }
            else
            {
                Globals.Config = new GlobalConfig();
                File.WriteAllText(GlobalConfigFile, JsonConvert.SerializeObject(Globals.Config, Formatting.Indented));
                Console.WriteLine("Created new global configuration file");
            }

            for (int i = 0; i < Globals.Config.ServerCount; i++)
            {
                string serverDir = $"Server-{i}";

                Directory.CreateDirectory(serverDir);

                string configPath = Path.Combine(serverDir, "config.json");
                Config config;

                if (File.Exists(configPath))
                {
                    config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath));
                }
                else
                {
                    config = new Config();
                    File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented));
                    Console.WriteLine($"Created new configuration file for Server-{i}");
                }

                Servers.Add(i, new Server(config, version, i));
            }

            if (Servers.GroupBy(entry => entry.Value.Port).Any(group => group.Count() > 1))
                throw new InvalidOperationException("Multiple servers cannot be binded to the same port");

            var serverTasks = Servers.Select(entry => entry.Value.StartServerAsync());
            InitConsoleInput();
            await Task.WhenAny(cancelKeyPress.Task, Task.WhenAll(serverTasks));

            if (!shutdownPending)
            {
                Console.WriteLine("Server(s) killed. Press any key to return...");
                Console.ReadKey(intercept: false);
            }
        }

        [Obsolete]
        private static void InitConsoleInput()
        {
            Task.Run(async () =>
            {
                Server currentServer = Servers.First().Value;
                await Task.Delay(2000);
                while (!shutdownPending)
                {
                    if (currentServer == null && Servers.Count == 0)
                        break;

                    string input = ConsoleIO.ReadLine();

                    if (input.StartsWith('.'))
                    {
                        if (input.StartsWith(".switch"))
                        {
                            string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length < 2)
                            {
                                ConsoleIO.WriteLine("Invalid server id");
                                continue;
                            }
                            if (!int.TryParse(parts[1], out int serverId))
                            {
                                ConsoleIO.WriteLine("Invalid server id");
                                continue;
                            }
                            if (!Servers.TryGetValue(serverId, out var server))
                            {
                                ConsoleIO.WriteLine("No server with given id found");
                                continue;
                            }

                            currentServer = server;
                            ConsoleIO.WriteLine($"Changed current server to {server.Id}");
                        }
                        else if (input.StartsWith(".execute"))
                        {
                            string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length < 3)
                            {
                                ConsoleIO.WriteLine("Invalid server id or command");
                                continue;
                            }
                            if (!int.TryParse(parts[1], out int serverId))
                            {
                                ConsoleIO.WriteLine("Invalid server id");
                                continue;
                            }
                            if (!Servers.TryGetValue(serverId, out var server))
                            {
                                ConsoleIO.WriteLine("No server with given id found");
                                continue;
                            }

                            ConsoleIO.WriteLine($"Executing command on Server-{server.Id}");
                            await server.ExecuteCommand(string.Join(' ', parts.Skip(2)));
                        }
                        else if (input.Equals(".clear", StringComparison.InvariantCultureIgnoreCase))
                        {
                            ConsoleIO.Reset();
                        }
                    }
                    else
                    {
                        await currentServer.ExecuteCommand(input);
                        if (input == "stop")
                        {
                            Servers.Remove(currentServer.Id);
                            currentServer = Servers.FirstOrDefault().Value;
                        }
                    }
                }
            });
        }

        [Obsolete]
        private static void OnConsoleCancelKeyPressed(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            StopProgram();
            cancelKeyPress.SetResult(true);
        }

        [Obsolete]
        private static bool OnConsoleEvent(NativeMethods.CtrlType ctrlType)
        {
            Console.WriteLine("Received {0}", ctrlType);
            StopProgram();
            return true;
        }

        /// <summary>
        /// Gracefully shuts sub-servers down and exits Obsidian.
        /// </summary>
        [Obsolete]
        private static void StopProgram()
        {
            shutdownPending = true;

            foreach (var (_, server) in Servers)
                server.StopServer();
        }

        // Cool startup console logo because that's cool
        // 10/10 -IGN
        private const string asciilogo =
            "\n" +
            "      ▄▄▄▄   ▄▄       ▄▄▄▄  ▀   ▄▄▄   ▐ ▄ \n" +
            "      ▐█ ▀█ ▐█ ▀  ██ ██  ██ ██ ▐█ ▀█  █▌▐█\n" +
            " ▄█▀▄ ▐█▀▀█▄▄▀▀▀█▄▐█ ▐█  ▐█▌▐█ ▄█▀▀█ ▐█▐▐▌\n" +
            "▐█▌ ▐▌██▄ ▐█▐█▄ ▐█▐█▌██  ██ ▐█▌▐█  ▐▌██▐█▌\n" +
            " ▀█▄▀  ▀▀▀▀  ▀▀▀▀ ▀▀▀▀▀▀▀▀  ▀▀▀ ▀  ▀ ▀▀ █ \n\n";
    }
}