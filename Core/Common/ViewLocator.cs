using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MnemoApp.Core.Common;

namespace MnemoApp;

public class ViewLocator : IDataTemplate
{

    public Control? Build(object? param)
    {
        if (param is null)
            return null;
        
        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null)
        {
            var control = (Control)Activator.CreateInstance(type)!;
            if (control.DataContext == null)
            {
                control.DataContext = param;
            }
            return control;
        }
        
        // Try alternative naming: remove "View" suffix and look for exact match
        var altName = param.GetType().FullName!.Replace("ViewModel", "", StringComparison.Ordinal);
        var altType = Type.GetType(altName);
        
        if (altType != null)
        {
            var control = (Control)Activator.CreateInstance(altType)!;
            if (control.DataContext == null)
            {
                control.DataContext = param;
            }
            return control;
        }
        
        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
