namespace VisualLab.Graphs;

/// <summary>A plugin variable: one value shared by every chain of the graph.</summary>
public sealed class GraphVariable
{
    /// <summary>Gets or sets the identifier.</summary>
    public string Id { get; set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the catalog type name.</summary>
    public string Type { get; set; }

    /// <summary>Gets or sets a value indicating whether the value survives restarts.</summary>
    public bool Persistent { get; set; }

    /// <summary>Gets or sets the variable default value.</summary>
    public object Default { get; set; }
}
