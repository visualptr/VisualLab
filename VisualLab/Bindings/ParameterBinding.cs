namespace VisualLab.Bindings;

/// <summary>One C# parameter of a generated method node, as the catalog spells it.</summary>
public sealed class ParameterBinding
{
    /// <summary>Gets or sets the parameter name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the catalog type name.</summary>
    public string Type { get; set; }

    /// <summary>Gets or sets a value indicating whether the parameter is optional.</summary>
    public bool Optional { get; set; }

    /// <summary>Gets or sets a value indicating whether the parameter is an output.</summary>
    public bool Out { get; set; }

    /// <summary>Gets or sets a value indicating whether the parameter accepts a variable argument array.</summary>
    public bool Params { get; set; }
}
