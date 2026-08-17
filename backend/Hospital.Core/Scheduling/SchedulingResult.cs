namespace Hospital.Core.Scheduling;

public enum SchedulingFailure
{
    None = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
}

public sealed record SchedulingResult<T>(
    T? Value,
    SchedulingFailure Failure,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Failure == SchedulingFailure.None;
}

public static class SchedulingResult
{
    public static SchedulingResult<T> Success<T>(T value) =>
        new(value, SchedulingFailure.None);

    public static SchedulingResult<T> Validation<T>(string code, string message) =>
        new(default, SchedulingFailure.Validation, code, message);

    public static SchedulingResult<T> NotFound<T>(string code, string message) =>
        new(default, SchedulingFailure.NotFound, code, message);

    public static SchedulingResult<T> Conflict<T>(string code, string message) =>
        new(default, SchedulingFailure.Conflict, code, message);
}
