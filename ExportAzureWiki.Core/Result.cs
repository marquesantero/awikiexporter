namespace ExportAzureWiki.Core;

public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    public Exception? Exception { get; }

    internal Result(bool isSuccess, T? value, string? error, Exception? exception = null)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        Exception = exception;
    }

    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        if (!IsSuccess)
            return Result.Failure<TNew>(Error ?? "Unknown error");

        try
        {
            return Result.Success(mapper(Value!));
        }
        catch (Exception ex)
        {
            return Result.Failure<TNew>(ex);
        }
    }

    public async Task<Result<TNew>> MapAsync<TNew>(Func<T, Task<TNew>> mapper)
    {
        if (!IsSuccess)
            return Result.Failure<TNew>(Error ?? "Unknown error");

        try
        {
            var result = await mapper(Value!);
            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<TNew>(ex);
        }
    }
}

public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }
    public Exception? Exception { get; }

    private Result(bool isSuccess, string? error, Exception? exception = null)
    {
        IsSuccess = isSuccess;
        Error = error;
        Exception = exception;
    }

    public static Result Success() => new(true, null);

    public static Result Failure(string error) => new(false, error);

    public static Result Failure(Exception exception) =>
        new(false, exception.Message, exception);

    public static Result<T> Success<T>(T value) => new(true, value, null);

    public static Result<T> Failure<T>(string error) => new(false, default, error);

    public static Result<T> Failure<T>(Exception exception) =>
        new(false, default, exception.Message, exception);
}
