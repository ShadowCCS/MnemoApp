using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Common;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Copies image files chosen by the user into the app-local images directory
/// so that block assets survive deletion of the original file.
/// </summary>
public sealed class ImageAssetService : IImageAssetService
{
    /// <inheritdoc/>
    public async Task<Result<string>> ImportAndCopyAsync(
        string sourcePath,
        string blockId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return Result<string>.Failure("Source path is empty.");

        if (!File.Exists(sourcePath))
            return Result<string>.Failure($"Source file not found: {sourcePath}");

        try
        {
            var imagesDir = MnemoAppPaths.GetImagesDirectory();
            Directory.CreateDirectory(imagesDir);

            var ext = Path.GetExtension(sourcePath);
            var dest = Path.Combine(imagesDir, blockId + ext);

            await Task.Run(() => File.Copy(sourcePath, dest, overwrite: true), cancellationToken)
                .ConfigureAwait(false);

            return Result<string>.Success(dest);
        }
        catch (OperationCanceledException)
        {
            return Result<string>.Failure("Image import was cancelled.");
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Failed to copy image: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteStoredFileAsync(
        string absolutePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
            return Result.Success();

        try
        {
            if (File.Exists(absolutePath))
                await Task.Run(() => File.Delete(absolutePath), cancellationToken)
                    .ConfigureAwait(false);

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failure("Delete was cancelled.");
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to delete stored image: {ex.Message}", ex);
        }
    }
}
