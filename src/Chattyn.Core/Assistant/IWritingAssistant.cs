namespace Chattyn.Core.Assistant;

public interface IWritingAssistant
{
    /// <summary>Checks one post. Throws <see cref="WritingAssistantException"/> with a user-facing message on failure.</summary>
    Task<CheckResult> CheckAsync(string post, AssistantOptions options, CancellationToken cancellationToken);
}

public enum AssistantErrorKind
{
    MissingKey,
    InvalidKey,
    NoCredit,
    RateLimited,
    Overloaded,
    Offline,
    Refused,
    Truncated,
    BadRequest,
    Unexpected,
}

/// <summary>A failed check, with a message meant for the overlay's status line.</summary>
public sealed class WritingAssistantException(AssistantErrorKind kind, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public AssistantErrorKind Kind { get; } = kind;
}
