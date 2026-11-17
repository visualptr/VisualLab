namespace VisualLab.Runtime;

/// <summary>A reference to a node's output pin.</summary>
public readonly struct NodeOutput
{
    /// <summary>Initializes a new instance of the <see cref="NodeOutput"/> struct.</summary>
    /// <param name="node">The runtime node.</param>
    /// <param name="pin">The pin ID.</param>
    public NodeOutput(RuntimeNode node, string pin)
    {
        Node = node;
        Pin = pin;
    }

    /// <summary>Gets the source runtime node.</summary>
    public RuntimeNode Node { get; }

    /// <summary>Gets the source output pin ID.</summary>
    public string Pin { get; }
}
