using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Resources;
using Xunit;

namespace Kaleidoscope.Tests.Resources;

public class LoadedContainerSetTests
{
    [Fact]
    public void Add_Then_Contains_ReturnsTrue()
    {
        var s = new LoadedContainerSet();
        s.Add(1001, Container.Inventory1);
        Assert.True(s.Contains(1001, Container.Inventory1));
        Assert.False(s.Contains(1001, Container.Inventory2));
    }

    [Fact]
    public void Remove_RemovesEntry()
    {
        var s = new LoadedContainerSet();
        s.Add(1001, Container.Inventory1);
        s.Remove(1001, Container.Inventory1);
        Assert.False(s.Contains(1001, Container.Inventory1));
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        var s = new LoadedContainerSet();
        s.Add(1001, Container.Inventory1);
        s.Add(5001, Container.RetainerPage1);
        s.Clear();
        Assert.False(s.Contains(1001, Container.Inventory1));
        Assert.False(s.Contains(5001, Container.RetainerPage1));
    }
}
