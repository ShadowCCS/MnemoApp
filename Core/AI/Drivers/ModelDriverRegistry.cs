using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MnemoApp.Core.AI.Models;

namespace MnemoApp.Core.AI.Drivers
{
    /// <summary>
    /// Registry for managing model drivers
    /// </summary>
    public class ModelDriverRegistry
    {
        private readonly Dictionary<string, IModelDriver> _drivers = new();
        private readonly List<IModelDriver> _registeredDrivers = new();
        private readonly Services.IAILogger _logger;

        public ModelDriverRegistry(Services.IAILogger? logger = null)
        {
            _logger = logger ?? new Services.DebugAILogger();
            
            // Register built-in drivers
            RegisterDriver(new LlamaCppDriver(_logger));
            RegisterDriver(new OpenAICompatibleDriver(_logger));
        }

        /// <summary>
        /// Register a model driver
        /// </summary>
        public void RegisterDriver(IModelDriver driver)
        {
            _registeredDrivers.Add(driver);
        }

        /// <summary>
        /// Get the appropriate driver for a model
        /// </summary>
        public IModelDriver? GetDriverForModel(AIModel model)
        {
            // Check if a specific driver is requested
            if (!string.IsNullOrEmpty(model.Capabilities?.CustomDriver))
            {
                var requestedDriver = _registeredDrivers.FirstOrDefault(d => 
                    d.DriverName.Equals(model.Capabilities.CustomDriver, StringComparison.OrdinalIgnoreCase));
                
                if (requestedDriver?.CanHandle(model) == true)
                    return requestedDriver;
            }

            // Check execution type mapping
            if (!string.IsNullOrEmpty(model.Capabilities?.ExecutionType))
            {
                var typeDriver = model.Capabilities.ExecutionType.ToLowerInvariant() switch
                {
                    "llamacpp" or "llama.cpp" => _registeredDrivers.FirstOrDefault(d => d.DriverName == "LlamaCppDriver"),
                    "openai" or "openaicompatible" => _registeredDrivers.FirstOrDefault(d => d.DriverName == "OpenAICompatibleDriver"),
                    _ => null
                };

                if (typeDriver?.CanHandle(model) == true)
                    return typeDriver;
            }

            // Auto-detect based on capabilities
            return _registeredDrivers.FirstOrDefault(driver => driver.CanHandle(model));
        }

        /// <summary>
        /// Initialize a driver for a model
        /// </summary>
        public async Task<bool> InitializeDriverForModel(AIModel model)
        {
            var driver = GetDriverForModel(model);
            if (driver == null)
                return false;

            var success = await driver.InitializeAsync(model);
            if (success)
            {
                _drivers[model.Manifest.Name] = driver;
            }

            return success;
        }

        /// <summary>
        /// Get the initialized driver for a model
        /// </summary>
        public IModelDriver? GetInitializedDriver(string modelName)
        {
            return _drivers.TryGetValue(modelName, out var driver) ? driver : null;
        }

        /// <summary>
        /// Shutdown driver for a model
        /// </summary>
        public async Task ShutdownDriverForModel(string modelName)
        {
            if (_drivers.TryGetValue(modelName, out var driver))
            {
                try
                {
                    var model = new AIModel 
                    { 
                        Manifest = new AIModelManifest { Name = modelName },
                        DirectoryPath = "",
                        ModelFileName = ""
                    };
                    
                    await driver.ShutdownAsync(model);
                }
                finally
                {
                    driver.Dispose();
                    _drivers.Remove(modelName);
                }
            }
        }

        /// <summary>
        /// Shutdown all drivers
        /// </summary>
        public async Task ShutdownAllAsync()
        {
            var modelNames = _drivers.Keys.ToList();
            foreach (var modelName in modelNames)
            {
                await ShutdownDriverForModel(modelName);
            }
        }

        /// <summary>
        /// Get all registered driver names
        /// </summary>
        public IEnumerable<string> GetRegisteredDriverNames()
        {
            return _registeredDrivers.Select(d => d.DriverName);
        }
    }
}
