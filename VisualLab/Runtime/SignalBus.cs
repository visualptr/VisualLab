using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using VisualLab.Runtime.Payloads;

namespace VisualLab.Runtime;

/// <summary>Delivers named signals between graphs.</summary>
public static class SignalBus
{
    private static readonly Dictionary<string, List<KeyValuePair<GraphRuntime, RuntimeNode>>> Listeners = new (StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Action<string, float, object>> Bridges = new (StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds a signal listener.</summary>
    /// <param name="name">The signal name.</param>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    public static void Listen(string name, GraphRuntime runtime, RuntimeNode node)
    {
        if (!Listeners.TryGetValue(name, out List<KeyValuePair<GraphRuntime, RuntimeNode>> list))
        {
            Listeners[name] = list = new List<KeyValuePair<GraphRuntime, RuntimeNode>>();
            Action<string, float, object> bridge = (text, number, player) =>
            {
                SignalCall call = new () { Text = text, Number = number, Player = player as Player };
                foreach (KeyValuePair<GraphRuntime, RuntimeNode> pair in list.ToList())
                    pair.Key.Fire(pair.Value, call);
            };
            Bridges[name] = bridge;
            SharedSignals.Listen(name, bridge);
        }

        list.Add(new KeyValuePair<GraphRuntime, RuntimeNode>(runtime, node));
    }

    /// <summary>Removes a signal listener.</summary>
    /// <param name="name">The signal name.</param>
    /// <param name="node">The runtime node.</param>
    public static void Forget(string name, RuntimeNode node)
    {
        if (Listeners.TryGetValue(name, out List<KeyValuePair<GraphRuntime, RuntimeNode>> list))
        {
            list.RemoveAll(pair => pair.Value == node);
            if (list.Count == 0)
            {
                SharedSignals.Forget(name, Bridges[name]);
                Bridges.Remove(name);
                Listeners.Remove(name);
            }
        }
    }

    /// <summary>Runs every listener of a signal.</summary>
    /// <param name="name">The signal name.</param>
    /// <param name="call">The signal values.</param>
    public static void Send(string name, SignalCall call) => SharedSignals.Send(name, call.Text, call.Number, call.Player);
}
