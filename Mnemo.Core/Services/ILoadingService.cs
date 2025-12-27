using System;

namespace Mnemo.Core.Services;

public interface ILoadingService
{
    void Show(string message = "Loading...");
    void ShowForTask(string taskId);
    void Hide();
    IDisposable BeginScope(string message = "Loading...");
}

