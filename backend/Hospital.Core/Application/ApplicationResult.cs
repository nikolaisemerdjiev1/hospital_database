namespace Hospital.Core.Application;

public enum ApplicationFailure
{
    None = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    DependencyUnavailable = 4,
}

public sealed record ApplicationResult<T>(
    T? Value,
    ApplicationFailure Failure,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Failure == ApplicationFailure.None;
}

public static class ApplicationResult
{
    public static ApplicationResult<T> Success<T>(T value) =>
        new(value, ApplicationFailure.None);

    public static ApplicationResult<T> Validation<T>(string code, string message) =>
        new(default, ApplicationFailure.Validation, code, message);

    public static ApplicationResult<T> NotFound<T>(string code, string message) =>
        new(default, ApplicationFailure.NotFound, code, message);

    public static ApplicationResult<T> Conflict<T>(string code, string message) =>
        new(default, ApplicationFailure.Conflict, code, message);

    public static ApplicationResult<T> DependencyUnavailable<T>(string code, string message) =>
        new(default, ApplicationFailure.DependencyUnavailable, code, message);
}
