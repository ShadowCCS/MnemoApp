using System.Threading.Tasks;

namespace Mnemo.Core.Services;

public interface ILateXEngine
{
    Task<object> RenderAsync(string tex, double fontSize = 16.0);
}