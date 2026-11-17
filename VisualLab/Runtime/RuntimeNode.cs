using System;
using System.Collections.Generic;
using VisualLab.Graphs;
using VisualLab.Runtime.Nodes;

namespace VisualLab.Runtime;

/// <summary>A graph node ready to run.</summary>
public sealed class RuntimeNode
{
    /// <summary>Gets or sets the stored node definition.</summary>
    public GraphNode Graph { get; set; }

    /// <summary>Gets or sets the node implementation.</summary>
    public NodeImplementation Implementation { get; set; }

    /// <summary>Gets the identifier.</summary>
    public string Id => Graph.Id;

    /// <summary>Gets data input pin → the output that feeds it.</summary>
    public Dictionary<string, NodeOutput> Inputs { get; } = new (StringComparer.Ordinal);

    /// <summary>Gets exec output pin → the node it runs next.</summary>
    public Dictionary<string, RuntimeNode> Successors { get; } = new (StringComparer.Ordinal);
}
