namespace DnnManager.Domain;

/// <summary>Generic operation result. No exceptions across layer boundaries.</summary>
public sealed record Result(bool Success, string? Error = null)
{
    public static Result Ok() => new(true);
    public static Result Fail(string error) => new(false, error);
}

public sealed record Result<T>(bool Success, T? Value, string? Error = null)
{
    public static Result<T> Ok(T value) => new(true, value);
    public static Result<T> Fail(string error) => new(false, default, error);
}
