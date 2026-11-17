using System;
using UnityEngine;
using VisualLab.Runtime;
using VisualLab.Runtime.Payloads;

namespace VisualLab.Runtime.Nodes;

/// <summary>event.keybind: fires when a player presses the bound key.</summary>
public sealed class KeybindNode : PayloadNode
{
    private int? _id;

    /// <summary>Gets the payload type that carries the outputs.</summary>
    protected override Type PayloadType => typeof(KeyPress);

    /// <summary>Adds the key binding to the server settings.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    public override void Start(GraphRuntime runtime, RuntimeNode node)
    {
        string label = Setting(node, "label", string.Empty).Trim();
        if (label.Length == 0)
            label = runtime.Name;

        _id = Keybinds.Add(runtime, node, label, Setting(node, "key", KeyCode.None), Setting(node, "hint", string.Empty));
    }

    /// <summary>Removes the key binding from the server settings.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    public override void Stop(GraphRuntime runtime, RuntimeNode node)
    {
        if (_id.HasValue)
            Keybinds.Remove(_id.Value);

        _id = null;
    }
}
