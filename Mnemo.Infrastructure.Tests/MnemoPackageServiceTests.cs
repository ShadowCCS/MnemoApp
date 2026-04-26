using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.Packaging;

namespace Mnemo.Infrastructure.Tests;

public sealed class MnemoPackageServiceTests
{
    [Fact]
    public async Task ExportAndPreviewAsync_WritesManifestAndDiscoversPayloadCounts()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"mnemo-test-{Guid.NewGuid():N}.mnemo");
        try
        {
            var service = new MnemoPackageService(
                [new StaticPayloadHandler("notes", 2), new StaticPayloadHandler("mindmaps", 1)],
                new NullLogger());

            var export = await service.ExportAsync(tempFile, new MnemoPackageExportOptions());
            Assert.True(export.IsSuccess);

            var preview = await service.PreviewAsync(tempFile);
            Assert.True(preview.IsSuccess);
            Assert.NotNull(preview.Value);
            Assert.Equal(2, preview.Value.DiscoveredCounts["notes"]);
            Assert.Equal(1, preview.Value.DiscoveredCounts["mindmaps"]);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ImportAsync_UnknownPayload_WarnsAndContinuesWhenNotStrict()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"mnemo-test-{Guid.NewGuid():N}.mnemo");
        try
        {
            var manifest = new MnemoPackageManifest
            {
                Entries =
                [
                    new MnemoPackageEntry
                    {
                        PayloadType = "unknown.payload",
                        ItemCount = 1,
                        Path = "payloads/unknown.payload"
                    }
                ]
            };

            await using (var file = File.Create(tempFile))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                var manifestEntry = zip.CreateEntry("manifest.json");
                await using (var stream = manifestEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(stream, manifest);
                }

                var dataEntry = zip.CreateEntry("payloads/unknown.payload/data.json");
                await using (var dataStream = dataEntry.Open())
                {
                    var data = Encoding.UTF8.GetBytes("{\"x\":1}");
                    await dataStream.WriteAsync(data);
                }
            }

            var service = new MnemoPackageService([], new NullLogger());
            var result = await service.ImportAsync(tempFile, new MnemoPackageImportOptions
            {
                StrictUnknownPayloads = false
            });

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Contains(result.Value.Warnings, w => w.Contains("Unknown payload type", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    private sealed class StaticPayloadHandler : IMnemoPayloadHandler
    {
        private readonly int _count;

        public StaticPayloadHandler(string payloadType, int count)
        {
            PayloadType = payloadType;
            _count = count;
        }

        public string PayloadType { get; }

        public Task<MnemoPayloadExportData> ExportAsync(MnemoPayloadExportContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MnemoPayloadExportData
            {
                ItemCount = _count,
                Files = new Dictionary<string, byte[]>
                {
                    ["data.json"] = Encoding.UTF8.GetBytes("{}")
                }
            });
        }

        public Task<MnemoPayloadImportResult> ImportAsync(MnemoPayloadImportContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MnemoPayloadImportResult { ImportedCount = _count });
        }
    }

    private sealed class NullLogger : ILoggerService
    {
        public void Log(Mnemo.Core.Enums.LogLevel level, string category, string message, Exception? exception = null) { }
    }
}
