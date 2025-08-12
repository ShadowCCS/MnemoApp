namespace MnemoApp.Data.Packaged
{
    public interface IMnemoPackageHandler
    {
        string Type { get; }
        void Import(string packagePath);
        void Export(string sourceDirectory, string destinationPackagePath);
    }
}


