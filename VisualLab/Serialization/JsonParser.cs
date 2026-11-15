using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VisualLab.Serialization;

/// <summary>A strict JSON parser.</summary>
internal sealed class JsonParser
{
    private const int MaxDepth = 128;
    private const string NumberCharacters = "+-0123456789.eE";

    private static readonly Regex NumberPattern = new (@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$");

    private readonly string _text;
    private int _depth;

    /// <summary>Initializes a new instance of the <see cref="JsonParser"/> class.</summary>
    /// <param name="text">The text to read.</param>
    public JsonParser(string text)
    {
        _text = text;
    }

    /// <summary>Gets the current JSON reading position.</summary>
    public int Position { get; private set; }

    private char Current => Position < _text.Length ? _text[Position] : '\0';

    /// <summary>Skips whitespace before the next token.</summary>
    public void SkipWhitespace()
    {
        while (Position < _text.Length && char.IsWhiteSpace(_text[Position]))
            Position++;
    }

    /// <summary>Reads the next JSON value.</summary>
    /// <returns>The parsed value.</returns>
    public object ReadValue()
    {
        if (++_depth > MaxDepth)
            throw new FormatException("JSON nesting is too deep.");

        try
        {
            return ReadToken();
        }
        finally
        {
            _depth--;
        }
    }

    private object ReadToken()
    {
        switch (Current)
        {
            case '{':
                return ReadObject();
            case '[':
                return ReadArray();
            case '"':
                return ReadString();
            case 't':
                Expect("true");
                return true;
            case 'f':
                Expect("false");
                return false;
            case 'n':
                Expect("null");
                return null;
            default:
                return ReadNumber();
        }
    }

    private void Expect(string word)
    {
        if (string.CompareOrdinal(_text, Position, word, 0, word.Length) != 0)
            throw new FormatException($"Invalid JSON at {Position}.");

        Position += word.Length;
    }

    private bool TryClose(char close)
    {
        SkipWhitespace();
        if (Current != close)
            return false;

        Position++;
        return true;
    }

    private bool ReadSeparator(char close)
    {
        SkipWhitespace();
        char c = Current;
        if (c != ',' && c != close)
            throw new FormatException($"Expected ',' or '{close}' at {Position}.");

        Position++;
        return c == ',';
    }

    private JsonObject ReadObject()
    {
        JsonObject result = new ();
        Position++;
        if (TryClose('}'))
            return result;

        do
        {
            SkipWhitespace();
            if (Current != '"')
                throw new FormatException($"Expected a key at {Position}.");

            string key = ReadString();
            SkipWhitespace();
            if (Current != ':')
                throw new FormatException($"Expected ':' at {Position}.");

            Position++;
            SkipWhitespace();
            result[key] = ReadValue();
        }
        while (ReadSeparator('}'));

        return result;
    }

    private List<object> ReadArray()
    {
        List<object> result = new ();
        Position++;
        if (TryClose(']'))
            return result;

        do
        {
            SkipWhitespace();
            result.Add(ReadValue());
        }
        while (ReadSeparator(']'));

        return result;
    }

    private string ReadString()
    {
        Position++;
        StringBuilder builder = new ();
        while (Position < _text.Length)
        {
            char c = _text[Position++];
            if (c == '"')
                return builder.ToString();

            if (c < 0x20)
                throw new FormatException("Unescaped control character in JSON string.");

            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            if (Position >= _text.Length)
                break;

            builder.Append(ReadEscape());
        }

        throw new FormatException("Unterminated string.");
    }

    private char ReadEscape()
    {
        char escape = _text[Position++];
        switch (escape)
        {
            case '"':
            case '\\':
            case '/':
                return escape;
            case 'b':
                return '\b';
            case 'f':
                return '\f';
            case 'n':
                return '\n';
            case 'r':
                return '\r';
            case 't':
                return '\t';
            case 'u':
                return ReadUnicode();
            default:
                throw new FormatException($"Bad escape at {Position}.");
        }
    }

    private char ReadUnicode()
    {
        if (Position + 4 > _text.Length)
            throw new FormatException("Bad \\u escape.");

        char c = (char)int.Parse(_text.Substring(Position, 4), NumberStyles.HexNumber);
        Position += 4;
        return c;
    }

    private double ReadNumber()
    {
        int start = Position;
        while (Position < _text.Length && NumberCharacters.IndexOf(_text[Position]) >= 0)
            Position++;

        if (start == Position)
            throw new FormatException($"Invalid JSON at {Position}.");

        string token = _text.Substring(start, Position - start);
        if (!NumberPattern.IsMatch(token))
            throw new FormatException("Invalid JSON number.");

        double value = double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (double.IsInfinity(value) || double.IsNaN(value))
            throw new FormatException("JSON number is outside the supported range.");

        return value;
    }
}
