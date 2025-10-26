using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace MnemoApp.Core.Extensions.Services
{
    /// <summary>
    /// Compiles source-based extensions using Roslyn
    /// </summary>
    public class SourceBasedExtensionCompiler
    {
        private readonly string _compilationCachePath;

        public SourceBasedExtensionCompiler()
        {
            _compilationCachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MnemoApp",
                "ExtensionCache"
            );
            
            Directory.CreateDirectory(_compilationCachePath);
        }

        /// <summary>
        /// Compile source files to an assembly
        /// </summary>
        public async Task<CompilationResult> CompileAsync(string extensionPath, string extensionName)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[COMPILER] Compiling source extension: {extensionName}");

                // Find all .cs files, excluding XAML code-behind files
                var sourceFiles = Directory.GetFiles(extensionPath, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("obj") && !f.Contains("bin") && !f.EndsWith(".axaml.cs"))
                    .ToArray();
                
                if (sourceFiles.Length == 0)
                {
                    return CompilationResult.Failed("No source files found");
                }

                System.Diagnostics.Debug.WriteLine($"[COMPILER] Found {sourceFiles.Length} source files");

                // Parse syntax trees
                var syntaxTrees = new List<SyntaxTree>();
                foreach (var sourceFile in sourceFiles)
                {
                    var code = await File.ReadAllTextAsync(sourceFile, System.Text.Encoding.UTF8);
                    var syntaxTree = CSharpSyntaxTree.ParseText(
                        code,
                        new CSharpParseOptions(LanguageVersion.Latest),
                        sourceFile,
                        System.Text.Encoding.UTF8
                    );
                    syntaxTrees.Add(syntaxTree);
                }

                // Get references
                var references = GetMetadataReferences(extensionPath);

                // Configure compilation
                var assemblyName = $"{extensionName}_{Guid.NewGuid():N}";
                var compilation = CSharpCompilation.Create(
                    assemblyName,
                    syntaxTrees,
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                        .WithOptimizationLevel(OptimizationLevel.Debug)
                        .WithPlatform(Platform.AnyCpu)
                        .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default)
                );

                // Compile to file in cache directory
                var outputPath = Path.Combine(_compilationCachePath, $"{extensionName}.dll");
                var pdbPath = Path.Combine(_compilationCachePath, $"{extensionName}.pdb");

                var emitOptions = new EmitOptions(
                    debugInformationFormat: DebugInformationFormat.PortablePdb
                );

                EmitResult result;
                using (var dllStream = File.Create(outputPath))
                using (var pdbStream = File.Create(pdbPath))
                {
                    result = compilation.Emit(
                        dllStream,
                        pdbStream,
                        options: emitOptions
                    );
                    
                    // Ensure everything is written to disk
                    dllStream.Flush();
                    pdbStream.Flush();
                }

                if (!result.Success)
                {
                    var errors = result.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => FormatDiagnostic(d))
                        .ToList();

                    System.Diagnostics.Debug.WriteLine($"[COMPILER] Compilation failed with {errors.Count} errors");
                    foreach (var error in errors)
                    {
                        System.Diagnostics.Debug.WriteLine($"[COMPILER] Error: {error}");
                    }

                    // Clean up failed compilation
                    try
                    {
                        if (File.Exists(outputPath)) File.Delete(outputPath);
                        if (File.Exists(pdbPath)) File.Delete(pdbPath);
                    }
                    catch { }

                    return CompilationResult.Failed("Compilation failed", errors);
                }

                // Log warnings
                var warnings = result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Warning)
                    .Select(d => FormatDiagnostic(d))
                    .ToList();

                foreach (var warning in warnings)
                {
                    System.Diagnostics.Debug.WriteLine($"[COMPILER] Warning: {warning}");
                }

                // Validate the compiled assembly can be loaded
                try
                {
                    using var fs = File.OpenRead(outputPath);
                    var assemblyBytes = new byte[fs.Length];
                    fs.Read(assemblyBytes, 0, assemblyBytes.Length);
                    _ = Assembly.Load(assemblyBytes);
                    System.Diagnostics.Debug.WriteLine($"[COMPILER] Successfully validated compiled assembly");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[COMPILER] Assembly validation failed: {ex.Message}");
                    try
                    {
                        if (File.Exists(outputPath)) File.Delete(outputPath);
                        if (File.Exists(pdbPath)) File.Delete(pdbPath);
                    }
                    catch { }
                    return CompilationResult.Failed($"Assembly validation failed: {ex.Message}");
                }

                System.Diagnostics.Debug.WriteLine($"[COMPILER] Successfully compiled {extensionName} to {outputPath}");

                return CompilationResult.CreateSuccess(outputPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[COMPILER] Compilation exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[COMPILER] Stack trace: {ex.StackTrace}");
                return CompilationResult.Failed($"Compilation exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a source extension needs recompilation
        /// </summary>
        public bool NeedsRecompilation(string extensionPath, string extensionName)
        {
            var cachedAssembly = Path.Combine(_compilationCachePath, $"{extensionName}.dll");
            
            if (!File.Exists(cachedAssembly))
            {
                return true;
            }

            var assemblyTime = File.GetLastWriteTimeUtc(cachedAssembly);
            var sourceFiles = Directory.GetFiles(extensionPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".axaml.cs"));

            foreach (var sourceFile in sourceFiles)
            {
                if (File.GetLastWriteTimeUtc(sourceFile) > assemblyTime)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Get metadata references for compilation
        /// </summary>
        private List<MetadataReference> GetMetadataReferences(string extensionPath)
        {
            var references = new List<MetadataReference>();

            // Add core framework assemblies from trusted platform
            var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator);

            var requiredAssemblies = new HashSet<string>
            {
                "System.Runtime",
                "System.Runtime.Extensions",
                "System.Collections",
                "System.Linq",
                "System.Console",
                "System.Threading",
                "System.Threading.Tasks",
                "System.IO.FileSystem",
                "netstandard",
                "System.Private.CoreLib"
            };

            foreach (var assembly in trustedAssemblies)
            {
                var assemblyName = Path.GetFileNameWithoutExtension(assembly);
                if (requiredAssemblies.Contains(assemblyName) ||
                    assemblyName.StartsWith("System.") ||
                    assemblyName.StartsWith("Microsoft."))
                {
                    try
                    {
                        references.Add(MetadataReference.CreateFromFile(assembly));
                    }
                    catch
                    {
                        // Skip if we can't load it
                    }
                }
            }

            // Add MnemoApp assembly
            var mnemoAppAssembly = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(mnemoAppAssembly))
            {
                references.Add(MetadataReference.CreateFromFile(mnemoAppAssembly));
            }

            // Add Avalonia assemblies
            try
            {
                var avaloniaAssemblies = new[]
                {
                    typeof(Avalonia.Application).Assembly,
                    typeof(Avalonia.Controls.Control).Assembly,
                    typeof(Avalonia.Layout.Layoutable).Assembly,
                    typeof(Avalonia.Interactivity.Interactive).Assembly,
                    typeof(Avalonia.Markup.Xaml.MarkupExtension).Assembly
                };

                foreach (var asm in avaloniaAssemblies)
                {
                    if (!string.IsNullOrEmpty(asm.Location))
                    {
                        references.Add(MetadataReference.CreateFromFile(asm.Location));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[COMPILER] Warning: Could not add Avalonia references: {ex.Message}");
            }

            // Add CommunityToolkit.Mvvm
            try
            {
                var mvvmAssembly = typeof(CommunityToolkit.Mvvm.ComponentModel.ObservableObject).Assembly;
                if (!string.IsNullOrEmpty(mvvmAssembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(mvvmAssembly.Location));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[COMPILER] Warning: Could not add MVVM references: {ex.Message}");
            }

            // Look for DLL dependencies in extension directory
            if (Directory.Exists(extensionPath))
            {
                var extensionDlls = Directory.GetFiles(extensionPath, "*.dll", SearchOption.AllDirectories);
                foreach (var dll in extensionDlls)
                {
                    try
                    {
                        references.Add(MetadataReference.CreateFromFile(dll));
                    }
                    catch
                    {
                        // Skip invalid DLLs
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[COMPILER] Added {references.Count} metadata references");
            return references;
        }

        /// <summary>
        /// Format a diagnostic message
        /// </summary>
        private string FormatDiagnostic(Diagnostic diagnostic)
        {
            var location = diagnostic.Location.GetLineSpan();
            var fileName = Path.GetFileName(location.Path);
            var line = location.StartLinePosition.Line + 1;
            var column = location.StartLinePosition.Character + 1;

            return $"{fileName}({line},{column}): {diagnostic.Severity.ToString().ToLower()} {diagnostic.Id}: {diagnostic.GetMessage()}";
        }
    }

    /// <summary>
    /// Result of source compilation
    /// </summary>
    public class CompilationResult
    {
        public bool Success { get; init; }
        public string? AssemblyPath { get; init; }
        public string? Error { get; init; }
        public List<string> Errors { get; init; } = new();

        public static CompilationResult CreateSuccess(string assemblyPath) => new()
        {
            Success = true,
            AssemblyPath = assemblyPath,
            Errors = new List<string>()
        };

        public static CompilationResult Failed(string error, List<string>? errors = null) => new()
        {
            Success = false,
            Error = error,
            Errors = errors ?? new List<string>()
        };
    }
}

