using System;

namespace Mnemo.Core.Models;

public class Result
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public Exception? Exception { get; }

    protected Result(bool isSuccess, string? errorMessage = null, Exception? exception = null)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Exception = exception;
    }

    public static Result Success() => new(true);
    public static Result Failure(string errorMessage, Exception? exception = null) => new(false, errorMessage, exception);
}

