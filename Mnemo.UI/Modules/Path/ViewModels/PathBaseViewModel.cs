using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Path.ViewModels;

public abstract class PathBaseViewModel : ViewModelBase
{
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private string _lastModified = string.Empty;
    public string LastModified
    {
        get => _lastModified;
        set => SetProperty(ref _lastModified, value);
    }

    private double _size;
    public double Size
    {
        get => _size;
        set => SetProperty(ref _size, value);
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    private string _category = "General";
    public string Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    public abstract bool IsFolder { get; }
}



