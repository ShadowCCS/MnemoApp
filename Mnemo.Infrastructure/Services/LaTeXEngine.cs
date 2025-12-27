using System.Threading.Tasks;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

public class LaTeXEngine : ILateXEngine
{
    public Task<dynamic> RenderAsync(string tex, double scale) => Task.FromResult<dynamic>(new object());
}

