using Obsidian.Hosting;
using Obsidian.Utilities;
using ObsidianCloud.ClientService;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

//TODO: Reach out to orchestrator and get server config
ServerConfiguration conf = new ServerConfiguration()
{

};

var service = new CloudClientServer(app.Lifetime, )


app.MapGet("/", () => "Hello World!");

app.Run();
