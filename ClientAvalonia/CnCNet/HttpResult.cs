using System;

namespace ClientAvalonia.CnCNet;

/// <summary>
/// Categorizes HTTP failures into <c>Network</c> (transient — DNS/timeout/refused,
/// retry may help) and <c>Business</c> (permanent — HTTP 4xx/5xx, decoding failed,
/// bad request, retry won't help). Lets callers decide whether to back off or surface
/// the error to the user.
/// </summary>
public sealed class HttpError
{
    public HttpErrorKind Kind { get; }

    /// <summary>Stable code for telemetry / branching, e.g. <c>"timeout"</c>, <c>"http-503"</c>.</summary>
    public string Code { get; }

    /// <summary>Human-readable message suitable for logs and user-facing surfaces.</summary>
    public string Message { get; }

    private HttpError(HttpErrorKind kind, string code, string message)
    {
        Kind = kind;
        Code = code ?? string.Empty;
        Message = message ?? string.Empty;
    }

    public static HttpError Network(string message, string code = "network")
        => new(HttpErrorKind.Network, code, message);

    public static HttpError Business(string code, string message)
        => new(HttpErrorKind.Business, code, message);

    public override string ToString() => $"[{Kind}:{Code}] {Message}";
}

public enum HttpErrorKind
{
    /// <summary>DNS / timeout / connection refused / interrupted. Retry may help.</summary>
    Network,

    /// <summary>HTTP 4xx / 5xx, decode failure, empty URL, etc. Retry will not help.</summary>
    Business,
}

/// <summary>
/// Typed HTTP result. <see cref="TryGetValue"/> returns <c>true</c> on success.
/// On failure, <see cref="Error"/> is non-null and categorizes the failure.
/// </summary>
public readonly struct HttpResult<T>
{
    public bool Success { get; }
    public T? Value { get; }
    public HttpError? Error { get; }

    private HttpResult(T value)
    {
        Success = true;
        Value = value;
        Error = null;
    }

    private HttpResult(HttpError error)
    {
        Success = false;
        Value = default;
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public static HttpResult<T> Ok(T value) => new(value);
    public static HttpResult<T> Err(HttpError error) => new(error);

    /// <summary>Pattern-match shortcut: assigns <paramref name="value"/> only on success.</summary>
    public bool TryGetValue(out T? value)
    {
        value = Value;
        return Success;
    }

    /// <summary>Map success value; pass through error unchanged.</summary>
    public HttpResult<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        if (Success)
            return HttpResult<TResult>.Ok(selector(Value!));
        return HttpResult<TResult>.Err(Error!);
    }
}
