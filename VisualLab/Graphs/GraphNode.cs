using System.Collections.Generic;
using VisualLab.Runtime;
using VisualLab.Serialization;

namespace VisualLab.Graphs;

/// <summary>A node as stored in a `visuallab/1` file.</summary>
public sealed class GraphNode
{
    /// <summary>Gets or sets the identifier.</summary>
    public string Id { get; set; }

    /// <summary>Gets or sets the catalog type name.</summary>
    public string Type { get; set; }

    /// <summary>Gets or sets the referenced variable ID.</summary>
    public string VarId { get; set; }

    /// <summary>Gets or sets the concrete type of a generic node.</summary>
    public string TypeArg { get; set; }

    /// <summary>Gets or sets the local function entry identifier.</summary>
    public string FunctionId { get; set; }

    /// <summary>Gets or sets the function parameters.</summary>
    public List<GraphParameter> Parameters { get; set; } = new ();

    /// <summary>Gets or sets the input literals.</summary>
    public JsonObject Values { get; set; } = new ();
}
