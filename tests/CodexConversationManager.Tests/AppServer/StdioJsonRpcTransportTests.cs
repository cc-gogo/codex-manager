using System.Diagnostics;
using System.Text.Json.Nodes;
using CodexConversationManager.Core.AppServer;
using Xunit;

namespace CodexConversationManager.Tests.AppServer;

public sealed class StdioJsonRpcTransportTests
{
    [Fact]
    public async Task Server_error_is_returned_as_typed_exception()
    {
        await using var transport = StartPowerShell(
            "$request = [Console]::In.ReadLine() | ConvertFrom-Json; " +
            "[Console]::Out.WriteLine(('{{\"id\":{0},\"error\":{{\"code\":321,\"message\":\"boom\"}}}}' -f $request.id))");

        var exception = await Assert.ThrowsAsync<AppServerRpcException>(() =>
            transport.SendRequestAsync(Request(1), TimeSpan.FromSeconds(2)));

        Assert.Equal(321, exception.Code);
        Assert.Contains("boom", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_response_fails_the_pending_request()
    {
        await using var transport = StartPowerShell(
            "$null = [Console]::In.ReadLine(); [Console]::Out.WriteLine('not-json')");

        await Assert.ThrowsAsync<AppServerProtocolException>(() =>
            transport.SendRequestAsync(Request(2), TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Unescaped_preview_is_discarded_while_preserving_the_codex_thread_name()
    {
        await using var transport = StartPowerShell(
            "$request = [Console]::In.ReadLine() | ConvertFrom-Json; " +
            "$response = '{\"id\":' + $request.id + ',\"result\":{\"data\":[{\"id\":\"thread-1\",\"preview\":\"say \"hello\" now\",\"ephemeral\":false,\"name\":\"call it \"quoted\"\",\"turns\":[]}],\"nextCursor\":null}}'; " +
            "[Console]::Out.WriteLine($response)");

        var result = await transport.SendRequestAsync(Request(4), TimeSpan.FromSeconds(2));

        Assert.Equal("thread-1", result?["data"]?[0]?["id"]?.GetValue<string>());
        Assert.Null(result?["data"]?[0]?["preview"]);
        Assert.False(result?["data"]?[0]?["ephemeral"]?.GetValue<bool>());
        Assert.Equal("call it \"quoted\"", result?["data"]?[0]?["name"]?.GetValue<string>());
    }

    [Fact]
    public async Task Missing_response_times_out_without_hanging()
    {
        await using var transport = StartPowerShell(
            "$null = [Console]::In.ReadLine(); Start-Sleep -Seconds 2");

        await Assert.ThrowsAsync<TimeoutException>(() =>
            transport.SendRequestAsync(Request(3), TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public async Task Dispose_closes_stdin_and_waits_for_owned_process_exit()
    {
        var transport = StartPowerShell("$null = [Console]::In.ReadToEnd()");
        var processId = transport.ProcessId;

        await transport.DisposeAsync();

        var lookupException = Record.Exception(() =>
        {
            using var process = Process.GetProcessById(processId);
        });
        Assert.IsType<ArgumentException>(lookupException);
    }

    private static StdioJsonRpcTransport StartPowerShell(string script) =>
        new("powershell.exe", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script]);

    private static JsonObject Request(long id) => new()
    {
        ["method"] = "test/request",
        ["id"] = id,
        ["params"] = new JsonObject()
    };
}
