using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MnemoApp.Core.Common;
using MnemoApp.Core.Extensions.Services;

namespace MnemoApp;

public class ViewLocator : IDataTemplate
{
    private static IExtensionService? _extensionService;

    /// <summary>
    /// Set the extension service for View resolution
    /// </summary>
    public static void SetExtensionService(IExtensionService extensionService)
    {
        _extensionService = extensionService;
    }

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

        // Try to find the View in extension assemblies
        if (_extensionService != null)
        {
            System.Diagnostics.Debug.WriteLine($"[ViewLocator] Searching extensions for View: {name}");
            var extensionType = FindViewInExtensions(name);
            if (extensionType != null)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewLocator] Found View in extension: {extensionType.FullName}");
                var control = (Control)Activator.CreateInstance(extensionType)!;
                if (control.DataContext == null)
                {
                    control.DataContext = param;
                }
                return control;
            }

            // Try alternative naming in extensions
            System.Diagnostics.Debug.WriteLine($"[ViewLocator] Trying alternative naming: {altName}");
            extensionType = FindViewInExtensions(altName);
            if (extensionType != null)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewLocator] Found View with alternative naming: {extensionType.FullName}");
                var control = (Control)Activator.CreateInstance(extensionType)!;
                if (control.DataContext == null)
                {
                    control.DataContext = param;
                }
                return control;
            }
        }
        
        return new TextBlock { Text = "Not Found: " + name };
    }

    private Type? FindViewInExtensions(string typeName)
    {
        if (_extensionService == null) return null;

        try
        {
            // Get all loaded extensions
            var loadedExtensions = _extensionService.GetLoadedExtensions();
            System.Diagnostics.Debug.WriteLine($"[ViewLocator] Found {loadedExtensions.Count} loaded extensions");
            
            foreach (var metadata in loadedExtensions)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewLocator] Checking extension: {metadata.Manifest.Name}");
                // Get the extension instance to access its assembly
                var instance = _extensionService.GetExtensionInstance(metadata.Manifest.Name);
                if (instance != null)
                {
                    var assembly = instance.GetType().Assembly;
                    System.Diagnostics.Debug.WriteLine($"[ViewLocator] Extension assembly: {assembly.FullName}");
                    
                    var type = assembly.GetType(typeName);
                    if (type != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ViewLocator] Found type via GetType: {type.FullName}");
                        return type;
                    }
                    
                    // Also try to find by name in all types
                    var allTypes = assembly.GetTypes();
                    System.Diagnostics.Debug.WriteLine($"[ViewLocator] Assembly has {allTypes.Length} types");
                    
                    // Log all types that contain "View" to help debug
                    var viewTypes = allTypes.Where(t => t.Name.Contains("View")).ToList();
                    System.Diagnostics.Debug.WriteLine($"[ViewLocator] View types in assembly: {string.Join(", ", viewTypes.Select(t => t.FullName))}");
                    
                    var matchingType = allTypes.FirstOrDefault(t => t.FullName == typeName);
                    if (matchingType != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ViewLocator] Found type via enumeration: {matchingType.FullName}");
                        return matchingType;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ViewLocator] Error searching extensions for type {typeName}: {ex.Message}");
        }

        return null;
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
