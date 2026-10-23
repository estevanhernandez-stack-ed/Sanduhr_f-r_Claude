using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text.RegularExpressions;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

// DockerPipeBoundaryTests counts the container path templates in this file: only PathFor may hold them.

/// <summary>The two Docker calls CI watchers make (spec ci-watchers-3.9 section 2).</summary>
internal interface IDockerApi
{
    /// <summary>The list call: the body, at most <see cref="DockerContainers.MaxBytes"/>.</summary>
    Task<byte[]> ListContainersAsync(CancellationToken ct);

    /// <summary>A runner's log, followed, with timestamps and the last 200 lines first.</summary>
    Task<Stream> FollowLogsAsync(string id, CancellationToken ct);
}

/// <summary>Why a Docker call failed: not reachable, the pipe refused this account, or Docker answered with an error status.</summary>
internal enum DockerFailure { Down, Denied, Refused }

/// <summary>
/// Docker Engine's API over the local named pipe <c>\\.\pipe\docker_engine</c>, as HTTP/1.1 through an
/// <see cref="HttpClient"/> whose connection is the pipe (no network, no TCP). Exactly two calls exist,
/// <see cref="Call"/>; nothing builds a path from outside it. Never inspect: it returns a container's
/// environment, where a runner keeps its registration token. <c>DockerPipeBoundaryTests</c> pins this file.
/// </summary>
internal sealed partial class DockerPipe : IDockerApi, IDisposable
{
    private const string PipeName = "docker_engine";
    private const string ApiVersion = "v1.43";

    internal enum Call { ListContainers, FollowLogs }

    private readonly HttpClient _http;

    public DockerPipe()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectCallback = async (_, ct) =>
            {
                var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    await pipe.ConnectAsync(2000, ct).ConfigureAwait(false);
                    return pipe;
                }
                catch
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            },
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri("http://docker/"), Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sanduhr");
    }

    internal static string PathFor(Call call, string? id = null) => call switch
    {
        Call.ListContainers => $"{ApiVersion}/containers/json",
        Call.FollowLogs when id is not null && IdPattern().IsMatch(id) =>
            $"{ApiVersion}/containers/{id}/logs?follow=1&stdout=1&stderr=1&timestamps=1&tail=200",
        _ => throw new ArgumentException("Not a call Sanduhr makes to Docker.", nameof(call)),
    };

    public async Task<byte[]> ListContainersAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await _http.GetAsync(PathFor(Call.ListContainers), HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        using var copy = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int n;
        while ((n = await body.ReadAsync(buffer, cts.Token).ConfigureAwait(false)) > 0)
        {
            if (copy.Length + n > DockerContainers.MaxBytes) throw new IOException("Docker's container list is too large.");
            copy.Write(buffer, 0, n);
        }
        return copy.ToArray();
    }

    public async Task<Stream> FollowLogsAsync(string id, CancellationToken ct)
    {
        // The 10 s limit covers the headers only; the returned stream follows the caller's token alone.
        using var headersCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headersCts.CancelAfter(TimeSpan.FromSeconds(10));
        var response = await _http.GetAsync(PathFor(Call.FollowLogs, id), HttpCompletionOption.ResponseHeadersRead, headersCts.Token).ConfigureAwait(false);
        try
        {
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>Access denied anywhere in the chain is the docker-users group; an HTTP status answer (a 400 for an
    /// API version the engine no longer takes, a 500) is Docker answering and refusing; everything else is Docker being down.</summary>
    internal static DockerFailure Classify(Exception e)
    {
        var answered = false;
        for (Exception? x = e; x is not null; x = x.InnerException)
        {
            if (x is UnauthorizedAccessException) return DockerFailure.Denied;
            if (x is HttpRequestException { StatusCode: not null }) answered = true;
        }
        return answered ? DockerFailure.Refused : DockerFailure.Down;
    }

    public void Dispose() => _http.Dispose();

    [GeneratedRegex("^[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
