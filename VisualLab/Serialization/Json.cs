using System;

namespace VisualLab.Serialization;

/// <summary>Reads and writes JSON.</summary>
public static class Json
{
    /// <summary>Parses a JSON value.</summary>
    /// <param name="text">The text to read.</param>
    /// <returns>The parsed value.</returns>
    public static object Parse(string text)
    {
        JsonParser parser = new (text);
        parser.SkipWhitespace();
        object value = parser.ReadValue();
        parser.SkipWhitespace();
        if (parser.Position != text.Length)
            throw new FormatException($"Unexpected text after JSON at {parser.Position}.");

        return value;
    }

    /// <summary>Parses a JSON object.</summary>
    /// <param name="text">The text to read.</param>
    /// <returns>The parsed object.</returns>
    public static JsonObject ParseObject(string text) => Parse(text) as JsonObject ?? throw new FormatException("Expected a JSON object.");

    /// <summary>Writes a value as JSON.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="pretty">Whether to indent the JSON.</param>
    /// <returns>The JSON text.</returns>
    public static string Write(object value, bool pretty = false)
    {
        JsonWriter writer = new (pretty);
        writer.Write(value);
        return writer.ToString();
    }
}
