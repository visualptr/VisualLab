using System;
using System.Collections.Generic;
using System.Linq;
using CommandSystem;
using LabApi.Features.Permissions;
using LabApi.Features.Wrappers;
using RemoteAdmin;
using VisualLab.Commands;
using VisualLab.Runtime;
using VisualLab.Runtime.Payloads;

namespace VisualLab.Runtime.Nodes;

/// <summary>event.command: registers a command and fires when someone runs it.</summary>
public sealed class CommandNode : PayloadNode
{
    private readonly List<CommandHandler> _handlers = new ();
    private GraphCommand _command;

    /// <summary>Gets the payload type that carries the outputs.</summary>
    protected override Type PayloadType => typeof(CommandCall);

    /// <summary>Registers the command.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    public override void Start(GraphRuntime runtime, RuntimeNode node)
    {
        string name = Setting(node, "name", string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        if (name.Length == 0 || name.Any(char.IsWhiteSpace))
            throw new ArgumentException("a command needs a name without spaces");

        string permission = Setting(node, "permission", string.Empty).Trim();
        string description = Setting(node, "description", string.Empty);
        if (description.Length == 0)
            description = "A " + runtime.Name + " command.";

        _command = new GraphCommand(name, description, (ArraySegment<string> arguments, ICommandSender sender, out string response) =>
        {
            if (permission.Length > 0 && sender is not ServerConsoleSender && !sender.HasPermission(permission))
            {
                response = "Missing permission: " + permission;
                return false;
            }

            List<string> list = arguments.ToList();
            CommandCall call = new () { Sender = Player.Get(sender), Arguments = list, Text = string.Join(" ", list), Source = sender };
            runtime.Fire(node, call);
            call.Returned = true;
            response = call.Response.Length > 0 ? call.Response : "Done.";
            return call.Success;
        });

        List<CommandHandler> wanted = new ();
        if (Setting(node, "remoteAdmin", true))
            wanted.Add(CommandProcessor.RemoteAdminCommandHandler);

        if (Setting(node, "playerConsole", false))
            wanted.Add(QueryProcessor.DotCommandHandler);

        if (Setting(node, "serverConsole", true))
            wanted.Add(GameCore.Console.ConsoleCommandHandler);

        foreach (CommandHandler handler in wanted)
        {
            if (handler.TryGetCommand(name, out _))
                throw new ArgumentException($"a command named \"{name}\" already exists; pick another name");
        }

        foreach (CommandHandler handler in wanted)
        {
            handler.RegisterCommand(_command);
            _handlers.Add(handler);
        }
    }

    /// <summary>Removes the command.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    public override void Stop(GraphRuntime runtime, RuntimeNode node)
    {
        foreach (CommandHandler handler in _handlers)
            handler.UnregisterCommand(_command);

        _handlers.Clear();
        _command = null;
    }
}
