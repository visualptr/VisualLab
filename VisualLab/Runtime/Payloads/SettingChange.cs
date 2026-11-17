using LabApi.Features.Wrappers;

namespace VisualLab.Runtime.Payloads;

/// <summary>A changed player-specific setting.</summary>
public sealed class SettingChange
{
    /// <summary>Gets or sets the player.</summary>
    public Player Player { get; set; }

    /// <summary>Gets or sets the slider value.</summary>
    public float Number { get; set; }

    /// <summary>Gets or sets a value indicating whether the toggle is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the dropdown index.</summary>
    public int Index { get; set; }

    /// <summary>Gets or sets the input text.</summary>
    public string Text { get; set; } = string.Empty;
}
