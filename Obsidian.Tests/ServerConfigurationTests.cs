using Obsidian.API.Configuration;
using Xunit;

namespace Obsidian.Tests;

public class ServerConfigurationTests
{
    [Fact]
    public void SimulationDistanceDoesNotFollowTheViewDistance()
    {
        // Whichever is set first, as configuration binding sets them in either order, like vanilla's two settings.
        Assert.Equal(6, new ServerConfiguration { ViewDistance = 12, SimulationDistance = 6 }.SimulationDistance);
        Assert.Equal(6, new ServerConfiguration { SimulationDistance = 6, ViewDistance = 12 }.SimulationDistance);
        Assert.Equal(16, new ServerConfiguration { ViewDistance = 8, SimulationDistance = 16 }.SimulationDistance);

        // At least the minimum, 5.
        Assert.Equal(5, new ServerConfiguration { SimulationDistance = 2 }.SimulationDistance);
    }
}
