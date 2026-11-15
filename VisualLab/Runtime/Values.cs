using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using LabApi.Features.Wrappers;
using UnityEngine;
using VisualLab.Bindings;

namespace VisualLab.Runtime;

/// <summary>Converts graph values to game types.</summary>
public static class Values
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Converts a pin value to a CLR type.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="type">The requested type.</param>
    /// <returns>The converted value.</returns>
    public static object Coerce(object value, Type type)
    {
        if (type.IsByRef)
            type = type.GetElementType();

        if (value is Unset)
            value = null;

        if (value == null)
            return type.IsValueType ? Activator.CreateInstance(type) : null;

        Type underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
            type = underlying;

        if (type == typeof(object) || type.IsInstanceOfType(value))
            return value;

        if (type == typeof(string))
            return ToText(value);

        if (type.IsEnum)
            return ToEnum(value, type);

        if (type == typeof(TimeSpan))
            return TimeSpan.FromSeconds(Convert.ToDouble(value, Invariant));

        if (type == typeof(Vector3))
            return value is string vector3 ? ParseVector3(vector3) : throw Mismatch(value, type);

        if (type == typeof(Vector2))
            return value is string vector2 ? ParseVector2(vector2) : throw Mismatch(value, type);

        if (type == typeof(Quaternion))
        {
            if (value is Vector3 euler)
                return Quaternion.Euler(euler);

            return value is string rotation ? Quaternion.Euler(ParseVector3(rotation)) : throw Mismatch(value, type);
        }

        if (type == typeof(Color))
            return value is string color ? ParseColor(color) : throw Mismatch(value, type);

        if (type == typeof(Color32))
            return (Color32)(Color)Coerce(value, typeof(Color));

        if ((type.IsPrimitive || type == typeof(decimal)) && value is IConvertible && value is not string)
        {
            if (type == typeof(bool))
                return value is bool ? value : throw Mismatch(value, type);

            if (IsInteger(type) && value is double or float)
                value = Math.Truncate(Convert.ToDouble(value, Invariant));

            return Convert.ChangeType(value, type, Invariant);
        }

        if (value is IEnumerable sequence && value is not string)
        {
            Type element = ElementType(type);
            if (element != null)
            {
                IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element));
                foreach (object item in sequence)
                    list.Add(Coerce(item, element));

                if (type.IsArray)
                {
                    Array array = Array.CreateInstance(element, list.Count);
                    list.CopyTo(array, 0);
                    return array;
                }

                if (type.IsInstanceOfType(list))
                    return list;

                ConstructorInfo constructor = type.GetConstructor(new[] { typeof(IEnumerable<>).MakeGenericType(element) });
                if (constructor != null)
                    return constructor.Invoke(new object[] { list });
            }
        }

        throw Mismatch(value, type);
    }

    /// <summary>Finds the element type of a supported collection.</summary>
    /// <param name="type">The requested type.</param>
    /// <returns>The element type, or null if unsupported.</returns>
    public static Type ElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        if (type.IsGenericType && type.GetGenericArguments().Length == 1)
        {
            Type definition = type.GetGenericTypeDefinition();
            if (definition == typeof(IEnumerable<>) || definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(ICollection<>) ||
                definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>) || definition == typeof(HashSet<>))
                return type.GetGenericArguments()[0];
        }

        return type == typeof(IEnumerable) ? typeof(object) : null;
    }

    /// <summary>Converts a name or number to an enum value.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="type">The requested type.</param>
    /// <returns>The enum value.</returns>
    public static object ToEnum(object value, Type type)
    {
        if (value is string name)
        {
            try
            {
                return Enum.Parse(type, name.Trim(), true);
            }
            catch (ArgumentException)
            {
                throw new ArgumentException($"\"{name}\" isn't a {type.Name} (try one of: {string.Join(", ", Enum.GetNames(type).Take(8))}…)");
            }
        }

        if (value.GetType().IsEnum && value.GetType() != type)
            return Enum.Parse(type, value.ToString(), true);

        return Enum.ToObject(type, Convert.ToInt64(value, Invariant));
    }

    /// <summary>Parses three comma-separated coordinates.</summary>
    /// <param name="text">The text to read.</param>
    /// <returns>The parsed vector.</returns>
    public static Vector3 ParseVector3(string text)
    {
        float[] parts = Floats(text, 3);
        return new Vector3(parts[0], parts[1], parts[2]);
    }

    /// <summary>Parses two comma-separated coordinates.</summary>
    /// <param name="text">The text to read.</param>
    /// <returns>The parsed vector.</returns>
    public static Vector2 ParseVector2(string text)
    {
        float[] parts = Floats(text, 2);
        return new Vector2(parts[0], parts[1]);
    }

    /// <summary>Parses an HTML color value.</summary>
    /// <param name="text">The text to read.</param>
    /// <returns>The parsed color.</returns>
    public static Color ParseColor(string text)
    {
        if (ColorUtility.TryParseHtmlString(text.Trim(), out Color color))
            return color;

        throw new FormatException($"\"{text}\" isn't a colour (use #rrggbb)");
    }

    /// <summary>Formats a pin value as readable text.</summary>
    /// <param name="value">The value to use.</param>
    /// <returns>The formatted text.</returns>
    public static string ToText(object value)
    {
        switch (value)
        {
            case null:
            case Unset:
                return string.Empty;
            case string text:
                return text;
            case Player player:
                return player.Nickname;
            case bool b:
                return b ? "true" : "false";
            case float f:
                return f.ToString(Invariant);
            case double d:
                return d.ToString(Invariant);
            case Vector3 v3:
                return $"{v3.x.ToString(Invariant)}, {v3.y.ToString(Invariant)}, {v3.z.ToString(Invariant)}";
            case Vector2 v2:
                return $"{v2.x.ToString(Invariant)}, {v2.y.ToString(Invariant)}";
            case Color color:
                return "#" + ColorUtility.ToHtmlStringRGB(color).ToLowerInvariant();
            case IEnumerable sequence:
                return string.Join(", ", sequence.Cast<object>().Select(ToText).ToArray());
            case IFormattable formattable:
                return formattable.ToString(null, Invariant);
            default:
                return value.ToString();
        }
    }

    /// <summary>Compares pin values, including numbers and game objects.</summary>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <returns>Whether the values are equal.</returns>
    public static bool Same(object a, object b)
    {
        if (a is Unset)
            a = null;

        if (b is Unset)
            b = null;

        if (a == null || b == null)
            return a == null && b == null;

        if (IsNumber(a) && IsNumber(b))
            return Convert.ToDouble(a, Invariant) == Convert.ToDouble(b, Invariant);

        if (a.GetType().IsEnum && b is string)
            b = ToEnum(b, a.GetType());
        else if (b.GetType().IsEnum && a is string)
            a = ToEnum(a, b.GetType());

        return Equals(a, b);
    }

    /// <summary>Checks that a value exists and has not been destroyed.</summary>
    /// <param name="value">The value to use.</param>
    /// <returns>Whether the value is valid.</returns>
    public static bool IsValid(object value)
    {
        if (value is null or Unset)
            return false;

        if (value is UnityEngine.Object unityObject)
            return unityObject != null;

        if (value is Player player)
            return player.ReferenceHub != null;

        return true;
    }

    /// <summary>Converts a variable default to its pin type.</summary>
    /// <param name="literal">The JSON literal.</param>
    /// <param name="pinType">The catalog pin type.</param>
    /// <returns>The converted value, or the original if unsupported.</returns>
    public static object Literal(object literal, string pinType)
    {
        if (literal == null)
            return null;

        Type type = TypeResolver.ForPin(pinType);
        try
        {
            return Coerce(literal, type);
        }
        catch
        {
            return literal;
        }
    }

    private static bool IsInteger(Type type) =>
        type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) || type == typeof(sbyte) ||
        type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort);

    private static bool IsNumber(object value) => value is double or float or int or long or short or ushort or byte or uint or sbyte or ulong;

    private static Exception Mismatch(object value, Type type) =>
        new InvalidCastException($"can't use {TypeResolver.CatalogName(value.GetType())} \"{ToText(value)}\" as {TypeResolver.CatalogName(type)}");

    private static float[] Floats(string text, int count)
    {
        string[] parts = text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        float[] result = new float[count];
        for (int i = 0; i < count && i < parts.Length; i++)
            result[i] = float.Parse(parts[i], NumberStyles.Float, Invariant);

        return result;
    }
}
