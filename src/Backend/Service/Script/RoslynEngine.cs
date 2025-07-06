using System.Data;
using System.Runtime.Loader;
using Conductor.Types;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Text;

namespace Conductor.Service.Script;


public class RoslynScriptEngine : IScriptEngine, IDisposable
{
    private readonly List<AssemblyLoadContext> _loadContexts = new();
    private readonly SemaphoreSlim _compilationSemaphore = new(1, 1);
    private bool _disposed = false;

    public async Task<Result<DataTable>> ExecuteAsync<T>(string script, T context, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var compilationResult = await CompileAsync(script, cancellationToken);
            if (!compilationResult.IsSuccessful)
                return Result<DataTable>.Err(compilationResult.Error);

            return Result<DataTable>.Ok(await compilationResult.Value.ExecuteAsync(context, cancellationToken));
        }
        catch (Exception ex)
        {
            return Result<DataTable>.Err(new Error($"Script execution failed: {ex.Message}", ex.StackTrace));
        }
    }

    public async Task<Result<IScript>> CompileAsync(string script, CancellationToken cancellationToken = default)
    {
        await _compilationSemaphore.WaitAsync(cancellationToken);
        try
        {
            var scriptClass = GenerateScriptClass(script);
            var compilation = CreateCompilation(scriptClass);

            using var ms = new MemoryStream();
            var result = compilation.Emit(ms);

            if (!result.Success)
            {
                var errors = string.Join("\n", result.Diagnostics
                    .Where(d => d.IsWarningAsError || d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.GetMessage()));

                return Result<IScript>.Err(new Error($"Compilation failed: {errors}"));
            }

            ms.Seek(0, SeekOrigin.Begin);

            var loadContext = new AssemblyLoadContext($"ScriptContext_{Guid.NewGuid()}", isCollectible: true);
            _loadContexts.Add(loadContext);

            var assembly = loadContext.LoadFromStream(ms);
            var scriptType = assembly.GetType("DynamicScript.GeneratedScript");

            if (scriptType is null)
                return Result<IScript>.Err(new Error("Could not find generated script type"));

            var scriptInstance = Activator.CreateInstance(scriptType) as IScript;
            if (scriptInstance is null)
                return Result<IScript>.Err(new Error("Could not create script instance"));

            return Result<IScript>.Ok(scriptInstance);
        }
        catch (Exception ex)
        {
            return Result<IScript>.Err(new Error($"Script compilation failed: {ex.Message}", ex.StackTrace));
        }
        finally
        {
            _compilationSemaphore.Release();
        }
    }

    private string GenerateScriptClass(string userScript)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Data;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Conductor.Service.Scripting;");
        sb.AppendLine("using Conductor.Service.Database;");
        sb.AppendLine("using Conductor.Types;");
        sb.AppendLine();
        sb.AppendLine("namespace DynamicScript");
        sb.AppendLine("{");
        sb.AppendLine("    public class GeneratedScript : BaseScript");
        sb.AppendLine("    {");
        sb.AppendLine("        public override async Task<DataTable> ExecuteAsync<T>(T context, CancellationToken cancellationToken = default)");
        sb.AppendLine("        {");
        sb.AppendLine("            // User script begins here");
        sb.AppendLine(userScript);
        sb.AppendLine("            // User script ends here");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private CSharpCompilation CreateCompilation(string sourceCode)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location), // System.Runtime
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location), // System.Console
            MetadataReference.CreateFromFile(typeof(DataTable).Assembly.Location), // System.Data
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location), // System.Linq
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location), // System.Threading.Tasks
            MetadataReference.CreateFromFile(typeof(CancellationToken).Assembly.Location), // System.Threading
            MetadataReference.CreateFromFile(Assembly.GetExecutingAssembly().Location), // Current assembly for BaseScript
        };

        // Add references to assemblies that are currently loaded
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
                catch
                {
                    // Ignore assemblies that can't be referenced
                }
            }
        }

        return CSharpCompilation.Create(
            $"DynamicScript_{Guid.NewGuid()}",
            new[] { syntaxTree },
            references.Distinct(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithPlatform(Platform.AnyCpu));
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var context in _loadContexts)
        {
            try
            {
                context.Unload();
            }
            catch
            {
                // Ignore unload errors
            }
        }

        _loadContexts.Clear();
        _compilationSemaphore.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}