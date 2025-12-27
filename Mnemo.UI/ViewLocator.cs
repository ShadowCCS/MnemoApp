using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI;

public class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<string, Type> _cache = new();

    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var vmType = param.GetType();
        var vmName = vmType.FullName!;
        
        if (_cache.TryGetValue(vmName, out var viewType))
        {
            return (Control)Activator.CreateInstance(viewType)!;
        }

        var viewName = vmName.Replace("ViewModel", "View", StringComparison.Ordinal);
        viewType = vmType.Assembly.GetType(viewName);

        if (viewType != null)
        {
            _cache[vmName] = viewType;
            return (Control)Activator.CreateInstance(viewType)!;
        }

        return new TextBlock { Text = "Not Found: " + viewName };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase || data?.GetType().Name.EndsWith("ViewModel") == true;
    }
}

