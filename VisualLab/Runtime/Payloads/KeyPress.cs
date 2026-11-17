using LabApi.Features.Wrappers;

namespace VisualLab.Runtime.Payloads;

/// <summary>One press of a graph key binding.</summary>
public sealed class KeyPress
{
    /// <summary>Gets or sets the player who pressed the key.</summary>
    public Player Player { get; set; }
}
