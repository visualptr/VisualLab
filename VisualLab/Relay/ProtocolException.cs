using System;
using VisualLab.Serialization;

namespace VisualLab.Relay;

/// <summary>A request the dashboard can recover from: sent back as `{ ok: false, error: { code, message, plugin? } }`.</summary>
public sealed class ProtocolException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ProtocolException"/> class.</summary>
    /// <param name="code">The protocol error code.</param>
    /// <param name="message">The message text.</param>
    /// <param name="plugin">The current plugin summary, if any.</param>
    public ProtocolException(string code, string message, JsonObject plugin = null)
        : base(message)
    {
        Code = code;
        Plugin = plugin;
    }

    /// <summary>Gets the protocol error code.</summary>
    public string Code { get; }

    /// <summary>Gets the current plugin summary, e.g. for a revision conflict.</summary>
    public JsonObject Plugin { get; }
}
