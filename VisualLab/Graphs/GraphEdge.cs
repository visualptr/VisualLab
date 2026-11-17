namespace VisualLab.Graphs;

/// <summary>A wire from an output pin to an input pin.</summary>
public sealed class GraphEdge
{
    /// <summary>Gets or sets the source node ID.</summary>
    public string FromNode { get; set; }

    /// <summary>Gets or sets the source output pin ID.</summary>
    public string FromPin { get; set; }

    /// <summary>Gets or sets the destination node ID.</summary>
    public string ToNode { get; set; }

    /// <summary>Gets or sets the destination input pin ID.</summary>
    public string ToPin { get; set; }
}
