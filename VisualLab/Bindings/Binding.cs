using System;
using System.Collections.Generic;
using System.Reflection;

namespace VisualLab.Bindings;

/// <summary>A generated node type, resolved (or not) against this server's LabAPI.</summary>
public sealed class Binding
{
    /// <summary>Gets or sets the catalog node type.</summary>
    public string NodeType { get; set; }

    /// <summary>Gets or sets the binding kind: event, property or method.</summary>
    public string Kind { get; set; }

    /// <summary>Gets or sets a value indicating whether the bound member is static.</summary>
    public bool Static { get; set; }

    /// <summary>Gets or sets a value indicating whether the bound method is an extension method.</summary>
    public bool Extension { get; set; }

    /// <summary>Gets or sets the property access: get or set.</summary>
    public string Access { get; set; }

    /// <summary>Gets or sets the catalog method parameters.</summary>
    public List<ParameterBinding> Parameters { get; set; } = new ();

    /// <summary>Gets or sets the resolved event.</summary>
    public EventInfo Event { get; set; }

    /// <summary>Gets or sets the event argument type.</summary>
    public Type ArgsType { get; set; }

    /// <summary>Gets or sets the resolved property.</summary>
    public PropertyInfo Property { get; set; }

    /// <summary>Gets or sets the resolved field.</summary>
    public FieldInfo Field { get; set; }

    /// <summary>Gets or sets the resolved method.</summary>
    public MethodInfo Method { get; set; }

    /// <summary>Gets or sets the CLR method parameters.</summary>
    public ParameterInfo[] MethodParams { get; set; }

    /// <summary>Gets or sets why it couldn't be resolved, e.g. the member doesn't exist in this LabAPI version.</summary>
    public string Error { get; set; }

    /// <summary>Gets or sets a value indicating whether the node is evaluated on demand (getters, queries) rather than run off an exec wire.</summary>
    public bool Pure { get; set; }

    /// <summary>Gets a value indicating whether the bound member needs an instance target.</summary>
    public bool NeedsTarget => !Static || Extension;
}
