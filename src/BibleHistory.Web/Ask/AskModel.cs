using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Options;

namespace BibleHistory.Web.Ask;

public class AskOptions
{
    /// <summary>Claude model that answers questions.</summary>
    public string Model { get; set; } = "claude-opus-5-5";
    /// <summary>low | medium | high | xhigh | max. Higher thinks longer and costs more.</summary>
    public string Effort { get; set; } = "medium";
    /// <summary>Upper bound on one answer, thinking included.</summary>
    public int MaxTokens { get; set; } = 16000;
    /// <summary>Questions each approved reader may ask per day (UTC). An admin can change it per person.</summary>
    public int DailyLimit { get; set; } = 20;
    public int MaxQuestionLength { get; set; } = 1000;
}

/// <summary>A piece of a streamed answer.</summary>
public abstract record AskChunk;
public sealed record AskText(string Text) : AskChunk;
public sealed record AskDone(string Model, string? StopReason, long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens) : AskChunk;

/// <summary>Answers one Bible question as a stream of text. Swapped for a fake in tests.</summary>
public interface IAskModel
{
    /// <summary>False until an API key is configured.</summary>
    bool Ready { get; }
    IAsyncEnumerable<AskChunk> AnswerAsync(string question, CancellationToken ct);
}

/// <summary>Claude through the official Anthropic SDK. The API key comes from Anthropic:ApiKey or ANTHROPIC_API_KEY.</summary>
public sealed class ClaudeAskModel(IOptions<AskOptions> options, IConfiguration config, IWebHostEnvironment env) : IAskModel
{
    readonly AskOptions opts = options.Value;
    readonly Lazy<AnthropicClient> client = new(() =>
    {
        var key = config["Anthropic:ApiKey"];
        return string.IsNullOrWhiteSpace(key) ? new AnthropicClient() : new AnthropicClient { ApiKey = key };
    });
    readonly Lazy<string> systemPrompt = new(() => File.ReadAllText(Path.Combine(env.ContentRootPath, "Ask", "ask-system.md")));

    public bool Ready =>
        !string.IsNullOrWhiteSpace(config["Anthropic:ApiKey"]) ||
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));

    public async IAsyncEnumerable<AskChunk> AnswerAsync(string question, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var p = new MessageCreateParams
        {
            Model = opts.Model,
            MaxTokens = opts.MaxTokens,
            OutputConfig = new BetaOutputConfig { Effort = ParseEffort(opts.Effort) },
            // If a safety classifier declines, the API retries on the model Anthropic recommends for that case, on the same stream
            Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
            Fallbacks = new Default(),
            // The instructions never change between questions, so they are cached
            System = new List<BetaTextBlockParam> { new() { Text = systemPrompt.Value, CacheControl = new BetaCacheControlEphemeral() } },
            Messages = [new BetaUserMessageParam { Content = question }],
        };

        string model = opts.Model;
        string? stop = null;
        long input = 0, output = 0, cacheRead = 0, cacheWrite = 0;
        await foreach (var ev in client.Value.Beta.Messages.CreateStreaming(p, ct))
        {
            if (ev.TryPickContentBlockDelta(out var cbd) && cbd.Delta.TryPickText(out var text))
            {
                yield return new AskText(text.Text);
            }
            else if (ev.TryPickStart(out var start))
            {
                model = start.Message.Model;
                input = start.Message.Usage.InputTokens;
                cacheRead = start.Message.Usage.CacheReadInputTokens ?? 0;
                cacheWrite = start.Message.Usage.CacheCreationInputTokens ?? 0;
            }
            else if (ev.TryPickDelta(out var md))
            {
                if (md.Delta.StopReason is { } sr) stop = sr.Raw();
                output = md.Usage.OutputTokens;
                if (md.Usage.InputTokens is { } i) input = i;
                if (md.Usage.CacheReadInputTokens is { } r) cacheRead = r;
                if (md.Usage.CacheCreationInputTokens is { } w) cacheWrite = w;
            }
        }
        yield return new AskDone(model, stop, input, output, cacheRead, cacheWrite);
    }

    static Effort ParseEffort(string s) => s.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => Effort.Medium,
    };
}

/// <summary>Estimated cost in US dollars, from Anthropic's published per-million-token prices.</summary>
public static class Pricing
{
    // US dollars per million tokens: input, output, and cache reads (cache writes cost 1.25x input)
    static readonly (string Prefix, decimal In, decimal Out, decimal CacheRead)[] Rates =
    [
        ("claude-opus-5-5", 4m, 20m, 0.20m),
        ("claude-opus-5", 5m, 25m, 0.50m),
        ("claude-opus-4", 5m, 25m, 0.50m),
        ("claude-sonnet-5-5", 2m, 10m, 0.10m),
        ("claude-sonnet-5", 2m, 10m, 0.20m),
        ("claude-haiku-5-5", 0.10m, 0.50m, 0.01m),
        ("claude-fable-5-2", 8m, 40m, 0.20m),
    ];

    public static decimal Estimate(string model, long input, long output, long cacheRead, long cacheWrite)
    {
        var r = Rates.FirstOrDefault(x => model.StartsWith(x.Prefix, StringComparison.Ordinal));
        if (r.Prefix is null) r = Rates[0];
        var cost = (input * r.In + output * r.Out + cacheRead * r.CacheRead + cacheWrite * r.In * 1.25m) / 1_000_000m;
        return Math.Round(cost, 6);
    }
}
