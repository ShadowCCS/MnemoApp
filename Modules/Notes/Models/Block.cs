using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MnemoApp.Modules.Notes.Models;

public class Block
{
    public string Id { get; set; } = string.Empty;
    public BlockType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    
    [JsonConverter(typeof(MetaDictionaryConverter))]
    public Dictionary<string, object> Meta { get; set; } = new();
    
    public int Order { get; set; }
}

public class MetaDictionaryConverter : JsonConverter<Dictionary<string, object>>
{
    public override Dictionary<string, object> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dictionary = new Dictionary<string, object>();
        
        if (reader.TokenType == JsonTokenType.Null)
        {
            return dictionary;
        }
        
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject, got {reader.TokenType}");
        }
        
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return dictionary;
            }
            
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException();
            }
            
            string propertyName = reader.GetString() ?? string.Empty;
            reader.Read();
            
            object? value = reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.TryGetInt32(out int intVal) ? intVal : reader.GetDouble(),
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.Null => null,
                JsonTokenType.StartArray => JsonSerializer.Deserialize<object[]>(ref reader, options),
                JsonTokenType.StartObject => JsonSerializer.Deserialize<Dictionary<string, object>>(ref reader, options) ?? new Dictionary<string, object>(),
                _ => throw new JsonException($"Unsupported token type: {reader.TokenType}")
            };
            
            dictionary[propertyName] = value ?? string.Empty;
        }
        
        throw new JsonException();
    }
    
    public override void Write(Utf8JsonWriter writer, Dictionary<string, object> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        
        foreach (var kvp in value)
        {
            writer.WritePropertyName(kvp.Key);
            JsonSerializer.Serialize(writer, kvp.Value, options);
        }
        
        writer.WriteEndObject();
    }
}

