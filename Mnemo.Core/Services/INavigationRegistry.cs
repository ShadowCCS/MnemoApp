using System;

namespace Mnemo.Core.Services;

public interface INavigationRegistry
{
    void RegisterRoute(string route, Type viewModelType);
}

