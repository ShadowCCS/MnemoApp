using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mnemo.Core.Models.Statistics;

namespace Mnemo.Infrastructure.Services.Statistics;

/// <summary>
/// Bidirectional conversion between <see cref="StatValue"/> and JSON. Storage uses a tagged form
/// so round-trips preserve the original type even when JSON would otherwise lose it (e.g. integer
/// vs. decimal, or a date stored as a string).
/// </summary>
internal static class StatValueJson
{
    private const string TypeProp = "t";
    private const string ValueProp = "v";

    /// <summary>Converts a <see cref="StatValue"/> to its tagged JSON node form for storage.</summary>
    public static JsonNode ToTagged(StatValue value)
    {
        var obj = new JsonObject();
        switch (value.Type)
        {
            case StatValueType.Boolean:
                obj[TypeProp] = "bool";
                obj[ValueProp] = value.AsBool();
                break;
            case StatValueType.Integer:
                obj[TypeProp] = "int";
                obj[ValueProp] = value.AsInt();
                break;
            case StatValueType.Decimal:
                obj[TypeProp] = "dec";
                obj[ValueProp] = value.AsDecimal();
                break;
            case StatValueType.String:
                obj[TypeProp] = "str";
                obj[ValueProp] = value.AsString();
                break;
            case StatValueType.DateTime:
                obj[TypeProp] = "dt";
                obj[ValueProp] = value.AsDateTime().ToString("O", CultureInfo.InvariantCulture);
                break;
            default:
                throw new InvalidOperationException($"Unsupported StatValueType: {value.Type}.");
        }
        return obj;
    }

    /// <summary>Restores a <see cref="StatValue"/> from its tagged JSON node form.</summary>
    public static StatValue FromTagged(JsonNode? node)
    {
        if (node is not JsonObject obj)
            throw new InvalidOperationException("Expected tagged StatValue object.");

        var typeTag = obj[TypeProp]?.GetValue<string>() ?? throw new InvalidOperationException("StatValue tag missing.");
        var v = obj[ValueProp];
        return typeTag switch
        {
            "bool" => StatValue.FromBool(v?.GetValue<bool>() ?? false),
            "int" => StatValue.FromInt(v?.GetValue<long>() ?? 0L),
            "dec" => StatValue.FromDecimal(v?.GetValue<double>() ?? 0d),
            "str" => StatValue.FromString(v?.GetValue<string>() ?? string.Empty),
            "dt" => StatValue.FromDateTime(ParseDateTime(v)),
            _ => throw new InvalidOperationException($"Unknown StatValue tag '{typeTag}'.")
        };
    }

    private static DateTimeOffset ParseDateTime(JsonNode? node)
    {
        var raw = node?.GetValue<string>();
        if (string.IsNullOrEmpty(raw)) return default;
        return DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    /// <summary>
    /// Best-effort coercion of a generic JSON-like value (used by the tool API) into a
    /// <see cref="StatValue"/>, optionally guided by an expected target type.
    /// </summary>
    public static bool TryCoerce(object? raw, StatValueType? expected, out StatValue result, out string? error)
    {
        result = default;
        error = null;

        if (raw is StatValue sv)
        {
            if (expected.HasValue && sv.Type != expected.Value)
            {
                error = $"Expected {expected.Value} but got {sv.Type}.";
                return false;
            }
            result = sv;
            return true;
        }

        if (raw is JsonElement je)
        {
            return TryCoerceJsonElement(je, expected, out result, out error);
        }

        switch (raw)
        {
            case null:
                error = "null is not a valid StatValue.";
                return false;
            case bool b:
                result = StatValue.FromBool(b);
                return CheckExpected(expected, StatValueType.Boolean, ref error);
            case sbyte or byte or short or ushort or int or uint or long:
                result = StatValue.FromInt(Convert.ToInt64(raw, CultureInfo.InvariantCulture));
                return CheckExpected(expected, StatValueType.Integer, ref error);
            case ulong ul:
                if (ul > long.MaxValue) { error = "ulong out of range for Integer."; return false; }
                result = StatValue.FromInt((long)ul);
                return CheckExpected(expected, StatValueType.Integer, ref error);
            case float or double or decimal:
                result = StatValue.FromDecimal(Convert.ToDouble(raw, CultureInfo.InvariantCulture));
                return CheckExpected(expected, StatValueType.Decimal, ref error);
            case string s:
                if (expected == StatValueType.DateTime &&
                    DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                {
                    result = StatValue.FromDateTime(dt);
                    return true;
                }
                if (expected == StatValueType.Integer && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                {
                    result = StatValue.FromInt(i);
                    return true;
                }
                if (expected == StatValueType.Decimal && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    result = StatValue.FromDecimal(d);
                    return true;
                }
                if (expected == StatValueType.Boolean && bool.TryParse(s, out var bv))
                {
                    result = StatValue.FromBool(bv);
                    return true;
                }
                result = StatValue.FromString(s);
                return CheckExpected(expected, StatValueType.String, ref error);
            case DateTime dtm:
                result = StatValue.FromDateTime(new DateTimeOffset(dtm.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dtm, DateTimeKind.Utc) : dtm));
                return CheckExpected(expected, StatValueType.DateTime, ref error);
            case DateTimeOffset dto:
                result = StatValue.FromDateTime(dto);
                return CheckExpected(expected, StatValueType.DateTime, ref error);
        }

        error = $"Unsupported value of type {raw.GetType().FullName}.";
        return false;
    }

    private static bool TryCoerceJsonElement(JsonElement je, StatValueType? expected, out StatValue result, out string? error)
    {
        result = default;
        error = null;
        switch (je.ValueKind)
        {
            case JsonValueKind.True:
                result = StatValue.FromBool(true);
                return CheckExpected(expected, StatValueType.Boolean, ref error);
            case JsonValueKind.False:
                result = StatValue.FromBool(false);
                return CheckExpected(expected, StatValueType.Boolean, ref error);
            case JsonValueKind.Number:
                if (expected == StatValueType.Decimal)
                {
                    if (je.TryGetDouble(out var d)) { result = StatValue.FromDecimal(d); return true; }
                }
                else if (expected == StatValueType.Integer)
                {
                    if (je.TryGetInt64(out var i)) { result = StatValue.FromInt(i); return true; }
                }
                if (je.TryGetInt64(out var asInt)) { result = StatValue.FromInt(asInt); }
                else if (je.TryGetDouble(out var asDouble)) { result = StatValue.FromDecimal(asDouble); }
                else { error = "Number out of range."; return false; }
                return CheckExpected(expected, result.Type, ref error);
            case JsonValueKind.String:
                var s = je.GetString() ?? string.Empty;
                if (expected == StatValueType.DateTime &&
                    DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                {
                    result = StatValue.FromDateTime(dt);
                    return true;
                }
                if (expected == StatValueType.Integer && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                {
                    result = StatValue.FromInt(iv);
                    return true;
                }
                if (expected == StatValueType.Decimal && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
                {
                    result = StatValue.FromDecimal(dv);
                    return true;
                }
                if (expected == StatValueType.Boolean && bool.TryParse(s, out var bv))
                {
                    result = StatValue.FromBool(bv);
                    return true;
                }
                result = StatValue.FromString(s);
                return CheckExpected(expected, StatValueType.String, ref error);
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                error = "null is not a valid StatValue.";
                return false;
            default:
                error = $"Unsupported JSON value kind: {je.ValueKind}.";
                return false;
        }
    }

    private static bool CheckExpected(StatValueType? expected, StatValueType actual, ref string? error)
    {
        if (!expected.HasValue || expected.Value == actual)
            return true;
        // Allow Integer -> Decimal widening as a convenience
        if (expected.Value == StatValueType.Decimal && actual == StatValueType.Integer)
            return true;
        error = $"Expected {expected.Value} but got {actual}.";
        return false;
    }
}
