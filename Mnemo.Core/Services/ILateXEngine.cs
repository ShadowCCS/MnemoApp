using System.Threading.Tasks;

namespace Mnemo.Core.Services;

public interface ILateXEngine
{
    Task<dynamic> RenderAsync(string tex, double scale);
}

