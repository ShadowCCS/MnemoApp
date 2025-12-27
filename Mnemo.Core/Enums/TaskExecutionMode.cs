namespace Mnemo.Core.Enums;

public enum TaskExecutionMode
{
    /// <summary>
    /// For light I/O tasks that can run in parallel.
    /// </summary>
    Parallel,

    /// <summary>
    /// For resource-intensive tasks (like Local AI) that should pause others.
    /// </summary>
    Exclusive
}





