namespace VisualLab.Runtime;

/// <summary>An input with no wire and no literal.</summary>
public sealed class Unset
{
    /// <summary>The shared marker for an unset input.</summary>
    public static readonly Unset Value = new ();

    private Unset()
    {
    }

    /// <summary>Returns the label for an unset input.</summary>
    /// <returns>The unset label.</returns>
    public override string ToString() => "(unset)";
}
