using System;
using VisualLab.Runtime;
using VisualLab.Serialization;

namespace VisualLab.Storage;

/// <summary>One graph plugin on disk: its file, its index state and its running runtime.</summary>
internal sealed class StoredPlugin
{
    /// <summary>Gets or sets the identifier.</summary>
    public string Id { get; set; }

    /// <summary>Gets or sets the most recent graph error.</summary>
    public string Error { get; set; }

    /// <summary>Gets or sets the stored graph JSON.</summary>
    public JsonObject Graph { get; set; }

    /// <summary>Gets or sets a value indicating whether the graph is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the saved graph revision.</summary>
    public double Revision { get; set; }

    /// <summary>Gets or sets the last update time in Unix milliseconds.</summary>
    public double UpdatedAt { get; set; }

    /// <summary>Gets or sets the values the graph keeps on disk.</summary>
    public SavedData Data { get; set; }

    /// <summary>Gets or sets the time until which chains report what they ran.</summary>
    public DateTime DebugUntil { get; set; }

    /// <summary>Gets or sets the run count last reported to dashboards.</summary>
    public long ReportedRuns { get; set; }

    /// <summary>Gets or sets the active graph runtime.</summary>
    public GraphRuntime Runtime { get; set; }
}
