using System;
using System.Collections.Generic;
using System.Linq;

namespace MnemoApp.Data.Packaged
{
    public class PackageRegistry
    {
        private readonly Dictionary<string, IMnemoPackageHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

        public void Register(IMnemoPackageHandler handler)
        {
            _handlers[handler.Type] = handler;
        }

        public IMnemoPackageHandler? Get(string type)
        {
            _handlers.TryGetValue(type, out var handler);
            return handler;
        }

        public IEnumerable<string> Types => _handlers.Keys.ToList();
    }
}


