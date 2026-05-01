using System;
using System.Globalization;

namespace Mnemo.Core.Models.Statistics;

/// <summary>
/// A typed scalar value stored on a <see cref="StatisticsRecord"/>. Backed by a
/// discriminated representation so callers (including extensions over the tool API)
/// cannot smuggle arbitrary objects through the field bag.
/// </summary>
public readonly struct StatValue : IEquatable<StatValue>
{
    /// <summary>The runtime type of the contained scalar.</summary>
    public StatValueType Type { get; }

    private readonly bool _bool;
    private readonly long _int;
    private readonly double _decimal;
    private readonly string? _string;
    private readonly DateTimeOffset _dateTime;

    private StatValue(StatValueType type, bool b, long i, double d, string? s, DateTimeOffset dt)
    {
        Type = type;
        _bool = b;
        _int = i;
        _decimal = d;
        _string = s;
        _dateTime = dt;
    }

    public static StatValue FromBool(bool value)
        => new(StatValueType.Boolean, value, 0, 0d, null, default);

    public static StatValue FromInt(long value)
        => new(StatValueType.Integer, false, value, 0d, null, default);

    public static StatValue FromDecimal(double value)
        => new(StatValueType.Decimal, false, 0, value, null, default);

    public static StatValue FromString(string value)
        => new(StatValueType.String, false, 0, 0d, value ?? string.Empty, default);

    public static StatValue FromDateTime(DateTimeOffset value)
        => new(StatValueType.DateTime, false, 0, 0d, null, value);

    /// <summary>Returns the contained <see cref="bool"/> value, or throws when the type does not match.</summary>
    public bool AsBool() => Type == StatValueType.Boolean
        ? _bool
        : throw new InvalidOperationException($"StatValue is {Type}, not Boolean.");

    /// <summary>Returns the contained <see cref="long"/> value, or throws when the type does not match.</summary>
    public long AsInt() => Type == StatValueType.Integer
        ? _int
        : throw new InvalidOperationException($"StatValue is {Type}, not Integer.");

    /// <summary>Returns the contained <see cref="double"/> value, or throws when the type does not match.</summary>
    public double AsDecimal() => Type == StatValueType.Decimal
        ? _decimal
        : throw new InvalidOperationException($"StatValue is {Type}, not Decimal.");

    /// <summary>Returns the contained <see cref="string"/> value, or throws when the type does not match.</summary>
    public string AsString() => Type == StatValueType.String
        ? _string ?? string.Empty
        : throw new InvalidOperationException($"StatValue is {Type}, not String.");

    /// <summary>Returns the contained <see cref="DateTimeOffset"/> value, or throws when the type does not match.</summary>
    public DateTimeOffset AsDateTime() => Type == StatValueType.DateTime
        ? _dateTime
        : throw new InvalidOperationException($"StatValue is {Type}, not DateTime.");

    /// <summary>Returns the value as a boxed CLR object (used by serializers and tool adapters).</summary>
    public object ToBoxed() => Type switch
    {
        StatValueType.Boolean => _bool,
        StatValueType.Integer => _int,
        StatValueType.Decimal => _decimal,
        StatValueType.String => _string ?? string.Empty,
        StatValueType.DateTime => _dateTime,
        _ => throw new InvalidOperationException($"Unknown StatValueType: {Type}.")
    };

    public override string ToString() => Type switch
    {
        StatValueType.Boolean => _bool ? "true" : "false",
        StatValueType.Integer => _int.ToString(CultureInfo.InvariantCulture),
        StatValueType.Decimal => _decimal.ToString("R", CultureInfo.InvariantCulture),
        StatValueType.String => _string ?? string.Empty,
        StatValueType.DateTime => _dateTime.ToString("O", CultureInfo.InvariantCulture),
        _ => string.Empty
    };

    public bool Equals(StatValue other)
    {
        if (Type != other.Type) return false;
        return Type switch
        {
            StatValueType.Boolean => _bool == other._bool,
            StatValueType.Integer => _int == other._int,
            StatValueType.Decimal => _decimal.Equals(other._decimal),
            StatValueType.String => string.Equals(_string ?? string.Empty, other._string ?? string.Empty, StringComparison.Ordinal),
            StatValueType.DateTime => _dateTime.Equals(other._dateTime),
            _ => false
        };
    }

    public override bool Equals(object? obj) => obj is StatValue v && Equals(v);

    public override int GetHashCode() => HashCode.Combine((int)Type, _bool, _int, _decimal, _string, _dateTime);

    public static bool operator ==(StatValue left, StatValue right) => left.Equals(right);
    public static bool operator !=(StatValue left, StatValue right) => !left.Equals(right);
}
