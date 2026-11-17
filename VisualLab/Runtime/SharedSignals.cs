using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace VisualLab.Runtime;

/// <summary>Shares signals with standalone C# exports in the server application domain.</summary>
internal static class SharedSignals
{
    private static Dictionary<string, List<Action<string, float, object>>> Bus
    {
        get
        {
            lock (AppDomain.CurrentDomain)
            {
                const string key = "VisualLab.Signals.v1";
                if (AppDomain.CurrentDomain.GetData(key) is Dictionary<string, List<Action<string, float, object>>> bus)
                    return bus;

                bus = new Dictionary<string, List<Action<string, float, object>>>(StringComparer.OrdinalIgnoreCase);
                AppDomain.CurrentDomain.SetData(key, bus);
                return bus;
            }
        }
    }

    /// <summary>Registers a signal callback.</summary>
    /// <param name="name">The signal name.</param>
    /// <param name="callback">The callback.</param>
    public static void Listen(string name, Action<string, float, object> callback)
    {
        lock (AppDomain.CurrentDomain)
        {
            if (!Bus.TryGetValue(name, out List<Action<string, float, object>> listeners))
                Bus[name] = listeners = new List<Action<string, float, object>>();

            listeners.Add(callback);
        }
    }

    /// <summary>Removes a signal callback.</summary>
    /// <param name="name">The signal name.</param>
    /// <param name="callback">The callback.</param>
    public static void Forget(string name, Action<string, float, object> callback)
    {
        lock (AppDomain.CurrentDomain)
        {
            if (!Bus.TryGetValue(name, out List<Action<string, float, object>> listeners))
                return;

            listeners.Remove(callback);
            if (listeners.Count == 0)
                Bus.Remove(name);
        }
    }

    /// <summary>Delivers a signal with a shared recursion limit.</summary>
    /// <param name="name">The signal name.</param>
    /// <param name="text">The text payload.</param>
    /// <param name="number">The numeric payload.</param>
    /// <param name="player">The player payload.</param>
    public static void Send(string name, string text, float number, object player)
    {
        Action<string, float, object>[] targets;
        lock (AppDomain.CurrentDomain)
            targets = Bus.TryGetValue(name, out List<Action<string, float, object>> listeners) ? listeners.ToArray() : Array.Empty<Action<string, float, object>>();

        LocalDataStoreSlot slot = Thread.GetNamedDataSlot("VisualLab.Signals.Depth.v1");
        int depth = Thread.GetData(slot) is int current ? current : 0;
        if (depth >= 16)
            throw new InvalidOperationException("Signals are sending each other in a loop.");

        Thread.SetData(slot, depth + 1);
        try
        {
            foreach (Action<string, float, object> target in targets)
                target(text, number, player);
        }
        finally
        {
            Thread.SetData(slot, depth);
        }
    }
}
