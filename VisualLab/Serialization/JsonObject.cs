using System;
using System.Collections.Generic;

namespace VisualLab.Serialization;

/// <summary>A JSON object.</summary>
public sealed class JsonObject : Dictionary<string, object>
{
    /// <summary>Initializes a new instance of the <see cref="JsonObject"/> class.</summary>
    public JsonObject()
        : base(StringComparer.Ordinal)
    {
    }

    /// <summary>Reads a string property.</summary>
    /// <param name="key">The property key.</param>
    /// <returns>The string, or null.</returns>
    public string Str(string key) => TryGetValue(key, out object value) ? value as string : null;

    /// <summary>Reads an object property.</summary>
    /// <param name="key">The property key.</param>
    /// <returns>The object, or null.</returns>
    public JsonObject Obj(string key) => TryGetValue(key, out object value) ? value as JsonObject : null;

    /// <summary>Reads an array property.</summary>
    /// <param name="key">The property key.</param>
    /// <returns>The array, or null.</returns>
    public List<object> Arr(string key) => TryGetValue(key, out object value) ? value as List<object> : null;

    /// <summary>Reads a boolean property.</summary>
    /// <param name="key">The property key.</param>
    /// <param name="fallback">The value to use when the property is missing or has the wrong type.</param>
    /// <returns>The boolean, or the fallback.</returns>
    public bool Bool(string key, bool fallback = false) => TryGetValue(key, out object value) && value is bool b ? b : fallback;

    /// <summary>Reads a number property.</summary>
    /// <param name="key">The property key.</param>
    /// <param name="fallback">The value to use when the property is missing or has the wrong type.</param>
    /// <returns>The number, or the fallback.</returns>
    public double Num(string key, double fallback = 0) => TryGetValue(key, out object value) && value is double d ? d : fallback;
}
