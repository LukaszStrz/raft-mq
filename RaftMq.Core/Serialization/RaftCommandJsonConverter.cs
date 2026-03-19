using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using RaftMq.Core.Interfaces;

namespace RaftMq.Core.Serialization;

/// <summary>
/// Handles polymorphic serialization of IRaftCommand objects using $type discriminator.
/// </summary>
public class RaftCommandJsonConverter : JsonConverter<IRaftCommand>
{
    private const string TypeDiscriminator = "$type";

    public override IRaftCommand? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDocument = JsonDocument.ParseValue(ref reader);
        var root = jsonDocument.RootElement;

        if (!root.TryGetProperty(TypeDiscriminator, out var typeProperty))
        {
            throw new JsonException($"Missing discriminator property '{TypeDiscriminator}'.");
        }

        var typeName = typeProperty.GetString();
        if (string.IsNullOrEmpty(typeName))
        {
            throw new JsonException($"Discriminator property '{TypeDiscriminator}' is empty.");
        }

        var type = Type.GetType(typeName);
        if (type == null || !typeof(IRaftCommand).IsAssignableFrom(type))
        {
            throw new JsonException($"Type '{typeName}' is not a valid IRaftCommand.");
        }

        return (IRaftCommand?)jsonDocument.Deserialize(type, options);
    }

    public override void Write(Utf8JsonWriter writer, IRaftCommand value, JsonSerializerOptions options)
    {
        var type = value.GetType();

        writer.WriteStartObject();
        
        // Write discriminator using FullName so it is somewhat assembly-agnostic, 
        // or AssemblyQualifiedName for strict evaluation.
        writer.WriteString(TypeDiscriminator, type.AssemblyQualifiedName);

        // Serialize original properties avoiding discriminator duplicate
        using var jsonDocument = JsonSerializer.SerializeToDocument(value, type, options);
        foreach (var property in jsonDocument.RootElement.EnumerateObject())
        {
            if (property.Name != TypeDiscriminator)
            {
                property.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
    }
}
