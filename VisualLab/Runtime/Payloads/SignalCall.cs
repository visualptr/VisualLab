using LabApi.Features.Wrappers;

namespace VisualLab.Runtime.Payloads;

/// <summary>The values carried by one signal.</summary>
public sealed class SignalCall
{
    /// <summary>Gets or sets the text value.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets the number value.</summary>
    public float Number { get; set; }

    /// <summary>Gets or sets the player value, if any.</summary>
    public Player Player { get; set; }
}
