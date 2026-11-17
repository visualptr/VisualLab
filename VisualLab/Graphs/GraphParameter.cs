namespace VisualLab.Graphs;

/// <summary>A named input parameter of a local function.</summary>
public sealed class GraphParameter
{
    /// <summary>Gets or sets the stable pin identifier.</summary>
    public string Id { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the concrete pin type.</summary>
    public string Type { get; set; }
}
