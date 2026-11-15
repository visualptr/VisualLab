using System;
using System.Collections.Generic;
using LabApi.Features.Console;
using VisualLab.Serialization;

namespace VisualLab.Logging;

/// <summary>Logs graph messages to the console and the dashboard.</summary>
public static class RuntimeLog
{
    private const int MaxTrackedMessages = 2000;

    private static readonly TimeSpan Throttle = TimeSpan.FromSeconds(5);
    private static readonly Dictionary<string, DateTime> LastWritten = new (StringComparer.Ordinal);

    /// <summary>Occurs when a graph runtime message is ready for dashboards.</summary>
    public static event Action<JsonObject> Pushed;

    /// <summary>Sends a graph message to the console and dashboard.</summary>
    /// <param name="pluginId">The graph plugin ID.</param>
    /// <param name="pluginName">The graph name.</param>
    /// <param name="level">The message severity.</param>
    /// <param name="message">The message text.</param>
    /// <param name="nodeId">The related node ID, if any.</param>
    public static void Write(string pluginId, string pluginName, LogLevel level, string message, string nodeId = null)
    {
        if (IsThrottled(pluginId + "\n" + nodeId + "\n" + message))
            return;

        WriteToConsole(level, $"[VisualLab] [{pluginName}] {message}" + (nodeId != null ? $" (node {nodeId})" : string.Empty));

        JsonObject push = new ()
        {
            ["event"] = "log",
            ["pluginId"] = pluginId,
            ["level"] = level.ToString().ToLowerInvariant(),
            ["message"] = message,
            ["time"] = (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        if (nodeId != null)
            push["nodeId"] = nodeId;

        Pushed?.Invoke(push);
    }

    private static bool IsThrottled(string key)
    {
        DateTime now = DateTime.UtcNow;
        if (LastWritten.TryGetValue(key, out DateTime last) && now - last < Throttle)
            return true;

        if (LastWritten.Count >= MaxTrackedMessages)
            LastWritten.Clear();

        LastWritten[key] = now;
        return false;
    }

    private static void WriteToConsole(LogLevel level, string text)
    {
        switch (level)
        {
            case LogLevel.Error:
                Logger.Error(text);
                break;
            case LogLevel.Warn:
                Logger.Warn(text);
                break;
            default:
                Logger.Info(text);
                break;
        }
    }
}
