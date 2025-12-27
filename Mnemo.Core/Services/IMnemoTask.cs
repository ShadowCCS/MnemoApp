using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Mnemo.Core.Services;

public enum TaskStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
    Paused
}

public interface IMnemoTask : INotifyPropertyChanged
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    double Progress { get; }
    string ProgressText { get; }
    TaskStatus Status { get; }
    IEnumerable<string> SubTaskIds { get; }
}


