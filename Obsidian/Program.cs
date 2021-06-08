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

                    if (dbKind != DatabaseKind.None && string.IsNullOrWhiteSpace(connectionString))
                    {
                        throw new InvalidOperationException("A value must be provided for \"connectionString\".");
                    }

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
                        .AddJsonSerialization()
                        .AddSingleton<ServerRegistry>()
                        .AddSingleton<ConsoleService>()
                        .AddHostedService<ObsidianCore>();
                })
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