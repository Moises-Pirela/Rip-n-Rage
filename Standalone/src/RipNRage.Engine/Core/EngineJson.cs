using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RipNRage.Engine
{
    /// <summary>
    /// System.Text.Json options for engine data files: public fields included, enums as strings, and
    /// compact converters for the math structs (<see cref="Vector3"/> / <see cref="Vector2"/> as
    /// <c>[x, y, z]</c>, <see cref="Color"/> as <c>[r, g, b, a]</c>). The structs expose computed
    /// properties (<c>normalized</c>, <c>magnitude</c>) that the default reflection serializer would
    /// otherwise try to walk, so they must never be serialized without these converters.
    /// </summary>
    public static class EngineJson
    {
        public static readonly JsonSerializerOptions Options = Create();

        public static JsonSerializerOptions Create()
        {
            var o = new JsonSerializerOptions
            {
                WriteIndented = true,
                IncludeFields = true,
                IgnoreReadOnlyProperties = true,
                IgnoreReadOnlyFields = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            o.Converters.Add(new JsonStringEnumConverter());
            o.Converters.Add(new Vector3Converter());
            o.Converters.Add(new Vector2Converter());
            o.Converters.Add(new ColorConverter());
            return o;
        }

        private static float[] ReadFloats(ref Utf8JsonReader reader, int max)
        {
            var result = new float[max];
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("expected an array of numbers");
            int i = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                float v = reader.TokenType == JsonTokenType.Number ? reader.GetSingle() : 0f;
                if (i < max) result[i] = v;
                i++;
            }
            return result;
        }

        public sealed class Vector3Converter : JsonConverter<Vector3>
        {
            public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var f = ReadFloats(ref reader, 3);
                return new Vector3(f[0], f[1], f[2]);
            }
            public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
            {
                writer.WriteStartArray(); writer.WriteNumberValue(Round(value.x)); writer.WriteNumberValue(Round(value.y)); writer.WriteNumberValue(Round(value.z)); writer.WriteEndArray();
            }
        }

        public sealed class Vector2Converter : JsonConverter<Vector2>
        {
            public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var f = ReadFloats(ref reader, 2);
                return new Vector2(f[0], f[1]);
            }
            public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
            {
                writer.WriteStartArray(); writer.WriteNumberValue(Round(value.x)); writer.WriteNumberValue(Round(value.y)); writer.WriteEndArray();
            }
        }

        public sealed class ColorConverter : JsonConverter<Color>
        {
            public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("expected [r, g, b, a]");
                float r = 0, g = 0, b = 0, a = 1f; int i = 0;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    float v = reader.TokenType == JsonTokenType.Number ? reader.GetSingle() : 0f;
                    switch (i) { case 0: r = v; break; case 1: g = v; break; case 2: b = v; break; case 3: a = v; break; }
                    i++;
                }
                return new Color(r, g, b, a);
            }
            public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
            {
                writer.WriteStartArray(); writer.WriteNumberValue(Round(value.r)); writer.WriteNumberValue(Round(value.g)); writer.WriteNumberValue(Round(value.b)); writer.WriteNumberValue(Round(value.a)); writer.WriteEndArray();
            }
        }

        /// <summary>Keeps the files readable (no 0.30000001192 noise).</summary>
        private static float Round(float v) => MathF.Round(v, 4);
    }
}
