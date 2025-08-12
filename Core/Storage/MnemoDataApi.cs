using System;

using MnemoApp.Data.Packaged;
using MnemoApp.Data.Runtime;

namespace MnemoApp.Core.Storage
{
    /// <summary>
    /// Facade exposed via MnemoAPI for unified storage access. Defaults to runtime scope.
    /// </summary>
    public class MnemoDataApi
    {
        private readonly IRuntimeStorage _runtimeStorage;
        private readonly MnemoApp.Data.Packaged.MnemoStorageManager? _storageManager;

        public MnemoDataApi(IRuntimeStorage runtimeStorage, MnemoApp.Data.Packaged.MnemoStorageManager? storageManager = null)
        {
            _runtimeStorage = runtimeStorage;
            _storageManager = storageManager;
        }

        public T? GetProperty<T>(string key, MnemoApp.Data.Runtime.StorageScope scope = MnemoApp.Data.Runtime.StorageScope.Runtime)
        {
            return scope switch
            {
                MnemoApp.Data.Runtime.StorageScope.Runtime => _runtimeStorage.GetProperty<T>(key),
                MnemoApp.Data.Runtime.StorageScope.Packaged => _storageManager?.HasActivePackage == true ? _storageManager.GetProperty<T>(key) : throw new InvalidOperationException("No active package opened"),
                _ => throw new NotSupportedException($"Scope {scope} not yet supported for direct GetProperty")
            };
        }

        public void SetProperty<T>(string key, T value, MnemoApp.Data.Runtime.StorageScope scope = MnemoApp.Data.Runtime.StorageScope.Runtime)
        {
            switch (scope)
            {
                case MnemoApp.Data.Runtime.StorageScope.Runtime:
                    _runtimeStorage.SetProperty(key, value);
                    break;
                case MnemoApp.Data.Runtime.StorageScope.Packaged:
                    if (_storageManager?.HasActivePackage != true) throw new InvalidOperationException("No active package opened");
                    _storageManager.SetProperty(key, value);
                    break;
                default:
                    throw new NotSupportedException($"Scope {scope} not yet supported for direct SetProperty");
            }
        }

        public bool HasProperty(string key, MnemoApp.Data.Runtime.StorageScope scope = MnemoApp.Data.Runtime.StorageScope.Runtime)
        {
            return scope switch
            {
                MnemoApp.Data.Runtime.StorageScope.Runtime => _runtimeStorage.HasProperty(key),
                MnemoApp.Data.Runtime.StorageScope.Packaged => _storageManager?.HasActivePackage == true && _storageManager.HasProperty(key),
                _ => throw new NotSupportedException($"Scope {scope} not yet supported for direct HasProperty")
            };
        }

        public void RemoveProperty(string key, MnemoApp.Data.Runtime.StorageScope scope = MnemoApp.Data.Runtime.StorageScope.Runtime)
        {
            switch (scope)
            {
                case MnemoApp.Data.Runtime.StorageScope.Runtime:
                    _runtimeStorage.RemoveProperty(key);
                    break;
                case MnemoApp.Data.Runtime.StorageScope.Packaged:
                    if (_storageManager?.HasActivePackage != true) throw new InvalidOperationException("No active package opened");
                    _storageManager.RemoveProperty(key);
                    break;
                default:
                    throw new NotSupportedException($"Scope {scope} not yet supported for direct RemoveProperty");
            }
        }

        public void AddProperty<T>(string key, T value, MnemoApp.Data.Runtime.StorageScope scope = MnemoApp.Data.Runtime.StorageScope.Runtime)
        {
            switch (scope)
            {
                case MnemoApp.Data.Runtime.StorageScope.Runtime:
                    _runtimeStorage.AddProperty(key, value);
                    break;
                case MnemoApp.Data.Runtime.StorageScope.Packaged:
                    if (_storageManager?.HasActivePackage != true) throw new InvalidOperationException("No active package opened");
                    _storageManager.SetProperty(key, value);
                    break;
                default:
                    throw new NotSupportedException($"Scope {scope} not yet supported for direct AddProperty");
            }
        }
    }
}


