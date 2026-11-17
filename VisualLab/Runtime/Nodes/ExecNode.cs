using VisualLab.Runtime;

namespace VisualLab.Runtime.Nodes;

/// <summary>Runs a built-in action node.</summary>
/// <param name="runtime">The graph runtime.</param>
/// <param name="node">The runtime node.</param>
/// <param name="frame">The current execution frame.</param>
public delegate void ExecFn(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame);

/// <summary>A hand-written node that runs off an exec wire.</summary>
public sealed class ExecNode : NodeImplementation
{
    private readonly ExecFn _run;

    /// <summary>Initializes a new instance of the <see cref="ExecNode"/> class.</summary>
    /// <param name="run">The action callback.</param>
    public ExecNode(ExecFn run)
    {
        _run = run;
    }

    /// <summary>Runs the action callback.</summary>
    /// <param name="runtime">The graph runtime.</param>
    /// <param name="node">The runtime node.</param>
    /// <param name="frame">The current execution frame.</param>
    public override void Run(GraphRuntime runtime, RuntimeNode node, ExecutionFrame frame) => _run(runtime, node, frame);
}
