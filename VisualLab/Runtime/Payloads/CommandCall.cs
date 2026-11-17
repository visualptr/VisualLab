using System.Collections.Generic;
using CommandSystem;
using LabApi.Features.Wrappers;

namespace VisualLab.Runtime.Payloads;

/// <summary>One run of a graph command.</summary>
public sealed class CommandCall
{
    private readonly List<string> _replies = new ();

    /// <summary>Gets or sets the player who ran the command, if any.</summary>
    public Player Sender { get; set; }

    /// <summary>Gets or sets the command arguments.</summary>
    public List<string> Arguments { get; set; } = new ();

    /// <summary>Gets or sets the arguments joined with spaces.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets the original command sender.</summary>
    public ICommandSender Source { get; set; }

    /// <summary>Gets or sets a value indicating whether the command succeeded.</summary>
    public bool Success { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the command already returned its response.</summary>
    public bool Returned { get; set; }

    /// <summary>Gets the response text collected so far.</summary>
    public string Response => string.Join("\n", _replies);

    /// <summary>Answers the sender now or with the command response.</summary>
    /// <param name="message">The reply text.</param>
    /// <param name="success">Whether the command succeeded.</param>
    public void Reply(string message, bool success)
    {
        if (Returned)
        {
            Source?.Respond(message, success);
            return;
        }

        _replies.Add(message);
        Success = success;
    }
}
