using System;
using System.Collections;
using System.Globalization;
using System.Text;

namespace VisualLab.Serialization;

/// <summary>A JSON writer.</summary>
internal sealed class JsonWriter
{
    private const double MaxSafeInteger = 9007199254740992;

    private readonly StringBuilder _builder = new ();
    private readonly bool _pretty;

    /// <summary>Initializes a new instance of the <see cref="JsonWriter"/> class.</summary>
    /// <param name="pretty">Whether to indent the JSON.</param>
    public JsonWriter(bool pretty)
    {
        _pretty = pretty;
    }

    /// <summary>Returns the JSON written so far.</summary>
    /// <returns>The JSON text.</returns>
    public override string ToString() => _builder.ToString();

    /// <summary>Appends a value as JSON.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="indent">The current indentation depth.</param>
    public void Write(object value, int indent = 0)
    {
        switch (value)
        {
            case null:
                _builder.Append("null");
                break;
            case string text:
                WriteString(text);
                break;
            case bool b:
                _builder.Append(b ? "true" : "false");
                break;
            case double d:
                WriteNumber(d);
                break;
            case float f:
                WriteNumber(f);
                break;
            case int or long or uint or ulong or short or ushort or byte or sbyte:
                _builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
            case IDictionary dictionary:
                WriteObject(dictionary, indent);
                break;
            case IEnumerable list:
                WriteArray(list, indent);
                break;
            default:
                WriteString(value.ToString());
                break;
        }
    }

    private void NewLine(int indent)
    {
        if (_pretty)
            _builder.Append('\n').Append(' ', indent * 2);
    }

    private void WriteObject(IDictionary dictionary, int indent)
    {
        _builder.Append('{');
        bool empty = true;
        foreach (DictionaryEntry entry in dictionary)
        {
            if (!empty)
                _builder.Append(',');

            empty = false;
            NewLine(indent + 1);
            WriteString((string)entry.Key);
            _builder.Append(_pretty ? ": " : ":");
            Write(entry.Value, indent + 1);
        }

        if (!empty)
            NewLine(indent);

        _builder.Append('}');
    }

    private void WriteArray(IEnumerable list, int indent)
    {
        _builder.Append('[');
        bool empty = true;
        foreach (object item in list)
        {
            if (!empty)
                _builder.Append(',');

            empty = false;
            NewLine(indent + 1);
            Write(item, indent + 1);
        }

        if (!empty)
            NewLine(indent);

        _builder.Append(']');
    }

    private void WriteNumber(double number)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
            _builder.Append("null");
        else if (Math.Abs(number) < MaxSafeInteger && number == Math.Floor(number))
            _builder.Append(((long)number).ToString(CultureInfo.InvariantCulture));
        else
            _builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
    }

    private void WriteString(string text)
    {
        _builder.Append('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '"':
                    _builder.Append("\\\"");
                    break;
                case '\\':
                    _builder.Append("\\\\");
                    break;
                case '\n':
                    _builder.Append("\\n");
                    break;
                case '\r':
                    _builder.Append("\\r");
                    break;
                case '\t':
                    _builder.Append("\\t");
                    break;
                default:
                    if (c < 0x20)
                        _builder.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        _builder.Append(c);

                    break;
            }
        }

        _builder.Append('"');
    }
}
