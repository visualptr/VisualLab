using System;
using CommandSystem;

namespace VisualLab.Commands;

/// <summary>Runs a graph command and returns its response.</summary>
/// <param name="arguments">The command arguments.</param>
/// <param name="sender">The command sender.</param>
/// <param name="response">The command response.</param>
/// <returns>Whether the command succeeded.</returns>
public delegate bool GraphCommandHandler(ArraySegment<string> arguments, ICommandSender sender, out string response);

/// <summary>A command registered by a graph.</summary>
public sealed class GraphCommand : ICommand
{
    private readonly GraphCommandHandler _handler;

    /// <summary>Initializes a new instance of the <see cref="GraphCommand"/> class.</summary>
    /// <param name="name">The command name.</param>
    /// <param name="description">The command description.</param>
    /// <param name="handler">The command callback.</param>
    public GraphCommand(string name, string description, GraphCommandHandler handler)
    {
        Command = name;
        Description = description;
        _handler = handler;
    }

    /// <summary>Gets the command name.</summary>
    public string Command { get; }

    /// <summary>Gets the alternate command names.</summary>
    public string[] Aliases { get; } = Array.Empty<string>();

    /// <summary>Gets the command description.</summary>
    public string Description { get; }

    /// <summary>Runs the graph command.</summary>
    /// <param name="arguments">The command arguments.</param>
    /// <param name="sender">The command sender.</param>
    /// <param name="response">The command response.</param>
    /// <returns>Whether the command succeeded.</returns>
    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response) => _handler(arguments, sender, out response);
}
