using System.Net;
using System.Net.Http;
using CodexConversationManager.App.Services;
using Xunit;

namespace CodexConversationManager.Tests.Services;

public sealed class UpdateCheckServiceTests
{
    [Fact]
    public async Task FallsBackToLatestReleaseRedirectWhenApiIsRateLimited()
    {
        using var client = new HttpClient(new StubHandler());
        var result = await new UpdateCheckService(client).CheckAsync();

        Assert.Equal("0.9.0", result.LatestVersion);
        Assert.Equal("https://github.com/cc-gogo/codex-manager/releases/tag/v0.9.0", result.ReleaseUrl);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Location = new Uri("https://github.com/cc-gogo/codex-manager/releases/tag/v0.9.0");
            return Task.FromResult(response);
        }
    }
}
