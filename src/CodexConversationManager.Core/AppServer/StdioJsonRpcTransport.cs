using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexConversationManager.Core.AppServer;

public sealed class StdioJsonRpcTransport : IJsonRpcTransport
{
    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };
    private readonly Process _process;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Task _readerTask;
    private readonly Task _stderrTask;
    private bool _disposed;

    public StdioJsonRpcTransport(string executablePath, IEnumerable<string>? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments ?? ["app-server", "--listen", "stdio://"])
        {
            startInfo.ArgumentList.Add(argument);
        }

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the Codex App Server process.");
        _readerTask = ReadResponsesAsync(_disposeCancellation.Token);
        _stderrTask = _process.StandardError.ReadToEndAsync(_disposeCancellation.Token);
    }

    public int ProcessId => _process.Id;

    public async Task<JsonNode?> SendRequestAsync(
        JsonObject request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (request["id"]?.GetValue<long>() is not { } id)
        {
            throw new ArgumentException("A request must contain a numeric ID.", nameof(request));
        }

        var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException($"Request ID {id} is already pending.");
        }

        try
        {
            await WriteAsync(request, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public Task SendNotificationAsync(
        JsonObject notification,
        CancellationToken cancellationToken = default) =>
        WriteAsync(notification, cancellationToken);

    private async Task WriteAsync(JsonObject message, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _process.StandardInput.WriteLineAsync(message.ToJsonString(CompactJson)).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadResponsesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                JsonObject response;
                try
                {
                    response = JsonNode.Parse(line) as JsonObject
                        ?? throw new JsonException("Response is not a JSON object.");
                }
                catch (JsonException exception)
                {
                    if (!TryParseWithDiscardedPreviews(line, out response, out var repairFailureProperty))
                    {
                        var previewMarkers = line.Split("\"preview\":", StringSplitOptions.None).Length - 1;
                        var followingMarkers = line.Split(",\"ephemeral\":", StringSplitOptions.None).Length - 1;
                        var nameFollowingFields = FindFollowingThreadFields(line, "\"name\":");
                        throw new AppServerProtocolException(
                            $"The App Server emitted malformed JSON (preview markers: {previewMarkers}; following markers: {followingMarkers}; repair failure property: {repairFailureProperty}; fields after name: {nameFollowingFields}).",
                            exception);
                    }
                }

                if (response["id"]?.GetValue<long>() is not { } id || !_pending.TryRemove(id, out var completion))
                {
                    continue;
                }

                if (response["error"] is JsonObject error)
                {
                    completion.TrySetException(new AppServerRpcException(
                        error["code"]?.GetValue<int>() ?? -1,
                        error["message"]?.GetValue<string>() ?? "Unknown error"));
                }
                else
                {
                    completion.TrySetResult(response["result"]?.DeepClone());
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                throw new EndOfStreamException("The App Server stdout stream closed.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(exception);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _process.StandardInput.Close();
            using var waitCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _process.WaitForExitAsync(waitCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _disposeCancellation.Cancel();
            await IgnoreCancellationAsync(_readerTask).ConfigureAwait(false);
            await IgnoreCancellationAsync(_stderrTask).ConfigureAwait(false);
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(new ObjectDisposedException(nameof(StdioJsonRpcTransport)));
            }

            _writeLock.Dispose();
            _disposeCancellation.Dispose();
            _process.Dispose();
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool TryParseWithDiscardedPreviews(
        string line,
        out JsonObject response,
        out string? repairFailureProperty)
    {
        if (!TryDiscardFieldValues(line, "preview", "ephemeral", out var withoutPreviews) ||
            !TryRepairStringFieldValues(withoutPreviews, "name", "turns", out var repaired))
        {
            response = null!;
            repairFailureProperty = null;
            return false;
        }

        try
        {
            response = JsonNode.Parse(repaired) as JsonObject
                ?? throw new JsonException("Response is not a JSON object.");
            repairFailureProperty = null;
            return true;
        }
        catch (JsonException)
        {
            response = null!;
            repairFailureProperty = FindLastProperty(repaired);
            return false;
        }
    }

    private static bool TryDiscardFieldValues(
        string json,
        string field,
        string followingField,
        out string repairedJson)
    {
        var marker = $"\"{field}\":";
        var followingMarker = $",\"{followingField}\":";
        var repaired = new StringBuilder(json.Length);
        var position = 0;
        var replaced = false;

        while (json.IndexOf(marker, position, StringComparison.Ordinal) is var markerIndex && markerIndex >= 0)
        {
            var valueStart = markerIndex + marker.Length;
            var boundary = json.IndexOf(followingMarker, valueStart, StringComparison.Ordinal);
            if (boundary < 0)
            {
                repairedJson = string.Empty;
                return false;
            }

            repaired.Append(json, position, markerIndex - position);
            repaired.Append(marker);
            repaired.Append("null");
            position = boundary;
            replaced = true;
        }

        if (!replaced)
        {
            repairedJson = json;
            return true;
        }

        repaired.Append(json, position, json.Length - position);
        repairedJson = repaired.ToString();
        return true;
    }

    private static bool TryRepairStringFieldValues(
        string json,
        string field,
        string followingField,
        out string repairedJson)
    {
        var marker = $"\"{field}\":";
        var followingMarker = $",\"{followingField}\":";
        var repaired = new StringBuilder(json.Length);
        var position = 0;

        while (json.IndexOf(marker, position, StringComparison.Ordinal) is var markerIndex && markerIndex >= 0)
        {
            var valueStart = markerIndex + marker.Length;
            var boundary = json.IndexOf(followingMarker, valueStart, StringComparison.Ordinal);
            if (boundary < 0)
            {
                repairedJson = string.Empty;
                return false;
            }

            var rawValue = json[valueStart..boundary];
            if (!TryNormalizeStringValue(rawValue, out var normalizedValue))
            {
                repairedJson = string.Empty;
                return false;
            }

            repaired.Append(json, position, markerIndex - position);
            repaired.Append(marker);
            repaired.Append(normalizedValue);
            position = boundary;
        }

        repaired.Append(json, position, json.Length - position);
        repairedJson = repaired.ToString();
        return true;
    }

    private static bool TryNormalizeStringValue(string rawValue, out string normalizedValue)
    {
        try
        {
            using var document = JsonDocument.Parse($"{{\"value\":{rawValue}}}");
            normalizedValue = JsonSerializer.Serialize(document.RootElement.GetProperty("value").GetString());
            return true;
        }
        catch (JsonException)
        {
            if (rawValue.Length >= 2 && rawValue[0] == '\"' && rawValue[^1] == '\"')
            {
                normalizedValue = JsonSerializer.Serialize(rawValue[1..^1]);
                return true;
            }

            normalizedValue = string.Empty;
            return false;
        }
    }

    private static string? FindLastProperty(string json)
    {
        string? lastProperty = null;
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    lastProperty = reader.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        return lastProperty;
    }

    private static string FindFollowingThreadFields(string json, string marker)
    {
        string[] candidates =
        [
            "path", "cwd", "cliVersion", "source", "agentNickname", "agentRole", "gitInfo",
            "turns", "forkedFromId", "sessionId", "createdAt", "updatedAt", "status"
        ];
        var results = new HashSet<string>(StringComparer.Ordinal);
        var position = 0;
        while ((position = json.IndexOf(marker, position, StringComparison.Ordinal)) >= 0)
        {
            var valueStart = position + marker.Length;
            var nearest = candidates
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Position = json.IndexOf($",\"{candidate}\":", valueStart, StringComparison.Ordinal)
                })
                .Where(item => item.Position >= 0)
                .MinBy(item => item.Position);
            if (nearest is not null)
            {
                results.Add(nearest.Candidate);
            }

            position = valueStart;
        }

        return string.Join(',', results.Order(StringComparer.Ordinal));
    }
}
