using System.Threading;
using System.Threading.Tasks;

namespace Mnemo.Core.Services;

public interface INotePdfLatexImageRenderer
{
    Task<NotePdfLatexRaster?> RenderLatexPngAsync(string latex, double fontSize, bool inline, CancellationToken cancellationToken = default);
}
