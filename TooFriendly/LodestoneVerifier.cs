using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace TooFriendly;

internal sealed record LodestoneLookupRequest(
    Guid FriendId,
    string Name,
    string World,
    uint RegionId);

internal sealed record LodestoneLookupResult(
    Guid FriendId,
    CharacterVerificationStatus Status,
    DateTime CompletedAtUtc,
    string Message);

internal sealed class LodestoneVerifier : IDisposable
{
    private static readonly Regex ResultRegex = new(
        "<p\\s+class=\"entry__name\"[^>]*>(?<name>.*?)</p>\\s*<p\\s+class=\"entry__world\"[^>]*>(?<world>.*?)</p>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.CultureInvariant);
    private static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(2);

    private readonly ConcurrentQueue<LodestoneLookupRequest> pending = new();
    private readonly ConcurrentQueue<LodestoneLookupResult> completed = new();
    private readonly SemaphoreSlim signal = new(0);
    private readonly object queuedGate = new();
    private readonly System.Collections.Generic.HashSet<Guid> queuedIds = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly HttpClient httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };
    private readonly IPluginLog log;
    private readonly Task worker;
    private TimeSpan nextRequestDelay = RequestInterval;
    private DateTime nextRequestUtc;
    private bool hasRequested;

    public LodestoneVerifier(IPluginLog log)
    {
        this.log = log;
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TooFriendly/0.1");
        worker = Task.Run(ProcessQueueAsync);
    }

    public int PendingCount
    {
        get
        {
            lock (queuedGate)
                return queuedIds.Count;
        }
    }

    public bool Enqueue(LodestoneLookupRequest request)
    {
        lock (queuedGate)
        {
            if (!queuedIds.Add(request.FriendId))
                return false;
        }

        pending.Enqueue(request);
        signal.Release();
        return true;
    }

    public bool TryDequeueResult(out LodestoneLookupResult result) =>
        completed.TryDequeue(out result!);

    public void Dispose()
    {
        cancellation.Cancel();
        signal.Release();
        worker.GetAwaiter().GetResult();
        httpClient.Dispose();
        signal.Dispose();
        cancellation.Dispose();
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            while (true)
            {
                await signal.WaitAsync(cancellation.Token);
                if (cancellation.IsCancellationRequested)
                    return;

                if (!pending.TryDequeue(out var request))
                    continue;

                LodestoneLookupResult result;
                try
                {
                    if (hasRequested)
                    {
                        var wait = nextRequestUtc - DateTime.UtcNow;
                        if (wait > TimeSpan.Zero)
                            await Task.Delay(wait, cancellation.Token);
                    }

                    result = await VerifyAsync(request, cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    log.Error(exception, "A TooFriendly Lodestone lookup failed.");
                    result = new LodestoneLookupResult(
                        request.FriendId,
                        CharacterVerificationStatus.LookupFailed,
                        DateTime.UtcNow,
                        "Lookup failed. Check the log or try again later.");
                }
                finally
                {
                    lock (queuedGate)
                        queuedIds.Remove(request.FriendId);
                }

                completed.Enqueue(result);
                nextRequestUtc = DateTime.UtcNow.Add(nextRequestDelay);
                hasRequested = true;
                nextRequestDelay = RequestInterval;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    private async Task<LodestoneLookupResult> VerifyAsync(
        LodestoneLookupRequest request,
        CancellationToken cancellationToken)
    {
        var host = request.RegionId switch
        {
            1 => "jp.finalfantasyxiv.com",
            2 => "na.finalfantasyxiv.com",
            3 => "eu.finalfantasyxiv.com",
            4 => "na.finalfantasyxiv.com",
            _ => null,
        };
        if (host == null)
        {
            return new LodestoneLookupResult(
                request.FriendId,
                CharacterVerificationStatus.LookupFailed,
                DateTime.UtcNow,
                "The saved Home World belongs to an unsupported Lodestone region.");
        }

        var url =
            $"https://{host}/lodestone/character/?q={Uri.EscapeDataString(request.Name)}&worldname={Uri.EscapeDataString(request.World)}";

        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ??
                                 (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ??
                                 TimeSpan.FromSeconds(30);
                nextRequestDelay = TimeSpan.FromSeconds(Math.Clamp(retryAfter.TotalSeconds, 2, 300));
            }

            var message = response.StatusCode == HttpStatusCode.TooManyRequests
                ? "Lodestone is rate-limiting checks. Try again later."
                : $"Lodestone returned HTTP {(int)response.StatusCode}. Try again later.";
            return new LodestoneLookupResult(
                request.FriendId,
                CharacterVerificationStatus.LookupFailed,
                DateTime.UtcNow,
                message);
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        foreach (Match match in ResultRegex.Matches(html))
        {
            var name = DecodeText(match.Groups["name"].Value);
            var world = DecodeText(match.Groups["world"].Value);
            var worldName = world.Split('[', 2)[0].Trim();
            if (string.Equals(name, request.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(worldName, request.World, StringComparison.OrdinalIgnoreCase))
            {
                return new LodestoneLookupResult(
                    request.FriendId,
                    CharacterVerificationStatus.Found,
                    DateTime.UtcNow,
                    string.Empty);
            }
        }

        if (html.Contains("parts__zero", StringComparison.Ordinal))
        {
            return new LodestoneLookupResult(
                request.FriendId,
                CharacterVerificationStatus.NotFound,
                DateTime.UtcNow,
                "Not found on Lodestone. The profile may be private; confirm before removing.");
        }

        return new LodestoneLookupResult(
            request.FriendId,
            CharacterVerificationStatus.LookupFailed,
            DateTime.UtcNow,
            "Lodestone's result format was not recognized. No existence result was recorded.");
    }

    private static string DecodeText(string html)
    {
        var withoutTags = HtmlTagRegex.Replace(html, string.Empty);
        return WebUtility.HtmlDecode(withoutTags).Trim();
    }
}
