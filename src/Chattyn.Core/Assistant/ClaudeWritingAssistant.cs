using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

namespace Chattyn.Core.Assistant;

/// <summary>
/// Checks a post with the Claude API: one independent request per check, structured JSON output.
/// Uses the beta endpoint so Sonnet/Opus can fall back to another model when they decline
/// ("default" fallbacks); on Haiku, which has no fallbacks, the request simply runs on Haiku.
/// </summary>
public sealed class ClaudeWritingAssistant(Func<string?> apiKeyProvider) : IWritingAssistant, IDisposable
{
    private static readonly Dictionary<string, JsonElement> Schema = CheckResultSchema.Build();

    private readonly Lock _lock = new();
    private AnthropicClient? _client;
    private string? _clientKey;

    public async Task<CheckResult> CheckAsync(string post, AssistantOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(post))
            throw new ArgumentException("Post is empty.", nameof(post));

        var client = GetClient();
        var parameters = new MessageCreateParams
        {
            Model = options.Model,
            // A ceiling, not a cost: thinking counts toward it, and a post is short.
            MaxTokens = 16000,
            Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
            Fallbacks = new Default(),
            System = PromptBuilder.BuildSystemPrompt(options),
            OutputConfig = new BetaOutputConfig
            {
                Effort = Effort.Low,
                Format = new BetaJsonOutputFormat { Schema = Schema },
            },
            Messages = [new BetaUserMessageParam { Content = PromptBuilder.BuildUserMessage(post) }],
        };

        BetaMessage response;
        try
        {
            response = await client.Beta.Messages.Create(parameters, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (Translate(ex, options) is { } translated)
        {
            throw translated;
        }

        if (response.StopReason == "refusal")
        {
            var category = response.StopDetails?.Category?.ToString();
            throw new WritingAssistantException(AssistantErrorKind.Refused,
                "Claude declined to check this post" + (string.IsNullOrEmpty(category) ? "." : $" ({category})."));
        }
        if (response.StopReason == "max_tokens")
        {
            throw new WritingAssistantException(AssistantErrorKind.Truncated,
                "The answer was cut off. Try a shorter post.");
        }

        var json = new StringBuilder();
        foreach (var block in response.Content)
        {
            // Read by type, not position: a reply can start with thinking blocks.
            if (block.TryPickText(out var text)) json.Append(text.Text);
        }

        try
        {
            return CheckResult.Parse(json.ToString());
        }
        catch (JsonException ex)
        {
            throw new WritingAssistantException(AssistantErrorKind.Unexpected,
                "Could not read Claude's answer. Try again.", ex);
        }
    }

    private AnthropicClient GetClient()
    {
        var key = apiKeyProvider();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new WritingAssistantException(AssistantErrorKind.MissingKey,
                "No API key. Create a .env file with ANTHROPIC_API_KEY=... (Settings > Create .env).");
        }

        lock (_lock)
        {
            if (_client is null || _clientKey != key)
            {
                (_client as IDisposable)?.Dispose();
                _client = new AnthropicClient
                {
                    ApiKey = key,
                    Timeout = TimeSpan.FromSeconds(60),
                };
                _clientKey = key;
            }
            return _client;
        }
    }

    /// <summary>Maps SDK errors to short messages for the overlay. Returns null for errors to rethrow as-is.</summary>
    private static WritingAssistantException? Translate(Exception ex, AssistantOptions options) => ex switch
    {
        AnthropicUnauthorizedException =>
            new(AssistantErrorKind.InvalidKey, "The API key in .env was rejected. Check it in the Claude Console.", ex),
        AnthropicPaymentRequiredException =>
            new(AssistantErrorKind.NoCredit, "No API credit left. Add credit in the Claude Console (Billing).", ex),
        AnthropicBadRequestException when ex.Message.Contains("credit balance", StringComparison.OrdinalIgnoreCase) =>
            new(AssistantErrorKind.NoCredit, "No API credit left. Add credit in the Claude Console (Billing).", ex),
        AnthropicNotFoundException =>
            new(AssistantErrorKind.BadRequest, $"Model '{options.Model}' was not found. Pick another model in Settings.", ex),
        AnthropicRateLimitException =>
            new(AssistantErrorKind.RateLimited, "Too many requests right now. Wait a few seconds and try again.", ex),
        AnthropicOverloadedException =>
            new(AssistantErrorKind.Overloaded, "Claude is overloaded right now. Try again in a moment.", ex),
        Anthropic5xxException =>
            new(AssistantErrorKind.Overloaded, "Claude's servers had a problem. Try again in a moment.", ex),
        AnthropicIOException =>
            new(AssistantErrorKind.Offline, "Can't reach api.anthropic.com. Check your internet connection.", ex),
        OperationCanceledException =>
            new(AssistantErrorKind.Offline, "The request timed out. Try again.", ex),
        AnthropicBadRequestException =>
            new(AssistantErrorKind.BadRequest, "Request rejected: " + Shorten(ex.Message), ex),
        AnthropicApiException =>
            new(AssistantErrorKind.Unexpected, "API error: " + Shorten(ex.Message), ex),
        _ => null,
    };

    private static string Shorten(string message) =>
        message.Length <= 200 ? message : message[..200] + "...";

    public void Dispose()
    {
        lock (_lock)
        {
            (_client as IDisposable)?.Dispose();
            _client = null;
            _clientKey = null;
        }
    }
}
