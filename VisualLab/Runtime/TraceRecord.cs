using System.Collections.Generic;
using VisualLab.Serialization;

namespace VisualLab.Runtime;

/// <summary>The nodes and values seen while one chain ran.</summary>
public sealed class TraceRecord
{
    private const int MaxNodes = 400;
    private const int MaxValues = 300;
    private const int MaxText = 80;

    private readonly List<object> _nodes = new ();
    private readonly JsonObject _values = new ();

    /// <summary>Records a node that ran.</summary>
    /// <param name="nodeId">The node ID.</param>
    public void Ran(string nodeId)
    {
        if (_nodes.Count < MaxNodes)
            _nodes.Add(nodeId);
    }

    /// <summary>Records a value read from an output pin.</summary>
    /// <param name="nodeId">The node ID.</param>
    /// <param name="pin">The pin ID.</param>
    /// <param name="value">The value that was read.</param>
    public void Read(string nodeId, string pin, object value)
    {
        string key = nodeId + ":" + pin;
        if (_values.Count >= MaxValues && !_values.ContainsKey(key))
            return;

        string text;
        try
        {
            text = value == null || value is Unset ? "none" : Values.ToText(value);
        }
        catch
        {
            text = "?";
        }

        _values[key] = text.Length > MaxText ? text.Substring(0, MaxText) + "…" : text;
    }

    /// <summary>Builds the dashboard push for this trace.</summary>
    /// <param name="pluginId">The graph plugin ID.</param>
    /// <param name="eventNodeId">The node that started the chain.</param>
    /// <param name="milliseconds">How long the chain took.</param>
    /// <param name="time">The Unix time in milliseconds.</param>
    /// <returns>The push message.</returns>
    public JsonObject ToPush(string pluginId, string eventNodeId, double milliseconds, double time) => new ()
    {
        ["event"] = "trace",
        ["pluginId"] = pluginId,
        ["eventNode"] = eventNodeId,
        ["nodes"] = _nodes,
        ["values"] = _values,
        ["ms"] = milliseconds,
        ["time"] = time,
    };
}
