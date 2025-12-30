using System;

namespace Mnemo.Core.Models;

public class Result<T> : Result
{
    public T? Value { get; }

    private Result(bool isSuccess, T? value, string? errorMessage = null, Exception? exception = null) 
        : base(isSuccess, errorMessage, exception)
    {
        Value = value;
    }

    public static Result<T> Success(T value) => new(true, value);
    public static new Result<T> Failure(string errorMessage, Exception? exception = null) => new(false, default, errorMessage, exception);

    public static implicit operator Result<T>(T value) => Success(value);
}




