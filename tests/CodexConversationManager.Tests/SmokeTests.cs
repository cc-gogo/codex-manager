using Xunit;

namespace CodexConversationManager.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Core_assembly_is_referenced()
    {
        Assert.NotNull(typeof(Core.AssemblyMarker).Assembly);
    }
}
