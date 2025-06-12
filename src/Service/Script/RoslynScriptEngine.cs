using System.Data;
using System.Runtime.Loader;
using Conductor.Types;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Text;
using System.Collections.Concurrent;

namespace Conductor.Service.Script;

public class RoslynScriptEngine : IScriptEngine, IDisposable
{
    private readonly ConcurrentBag<AssemblyLoadContext> loadContexts = [];
    private readonly SemaphoreSlim compilationSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, WeakReference<IScript>> scriptCache = new();
    private readonly List<MetadataReference> baseReference;
    private bool disposed = false;

    public RoslynScriptEngine()
    {
        baseReference = BuildBaseReferences();
    }

    public async Task<Result<DataTable>> ExecuteAsync<T>(string script, T context, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var scriptHash = ComputeHash(script);
            if (scriptCache.TryGetValue(scriptHash, out var weakRef) && 
                weakRef.TryGetTarget(out var cachedScript))
            {
                return Result<DataTable>.Ok(await cachedScript.ExecuteAsync(context, cancellationToken));
            }

            var compilationResult = await CompileAsync(script, cancellationToken);
            if (!compilationResult.IsSuccessful)
                return Result<DataTable>.Err(compilationResult.Error);

            scriptCache.TryAdd(scriptHash, new WeakReference<IScript>(compilationResult.Value));

            return Result<DataTable>.Ok(await compilationResult.Value.ExecuteAsync(context, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            return Result<DataTable>.Err(new Error("Script execution was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<DataTable>.Err(new Error($"Script execution failed: {ex.Message}", ex.StackTrace));
        }
    }

    public async Task<Result<IScript>> CompileAsync(string script, CancellationToken cancellationToken = default)
    {
        await compilationSemaphore.WaitAsync(cancellationToken);
        try
        {
            var scriptClass = GenerateScriptClass(script);
            var compilation = CreateCompilation(scriptClass);

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms, cancellationToken: cancellationToken);

            if (!emitResult.Success)
            {
                var errors = string.Join("\n", emitResult.Diagnostics
                    .Where(d => d.IsWarningAsError || d.Severity == DiagnosticSeverity.Error)
                    .Select(d => $"{d.Location}: {d.GetMessage()}"));

                return Result<IScript>.Err(new Error($"Compilation failed:\n{errors}"));
            }

            ms.Seek(0, SeekOrigin.Begin);

            var loadContext = new AssemblyLoadContext($"ScriptContext_{Guid.NewGuid()}", isCollectible: true);
            loadContexts.Add(loadContext);

            var assembly = loadContext.LoadFromStream(ms);
            var scriptType = assembly.GetType("DynamicScript.GeneratedScript");

            if (scriptType == null)
                return Result<IScript>.Err(new Error("Could not find generated script type"));

            var scriptInstance = Activator.CreateInstance(scriptType) as IScript;
            if (scriptInstance == null)
                return Result<IScript>.Err(new Error("Could not create script instance"));

            return Result<IScript>.Ok(scriptInstance);
        }
        catch (OperationCanceledException)
        {
            return Result<IScript>.Err(new Error("Script compilation was cancelled"));
        }
        catch (Exception ex)
        {
            return Result<IScript>.Err(new Error($"Script compilation failed: {ex.Message}", ex.StackTrace));
        }
        finally
        {
            compilationSemaphore.Release();
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
        sb.AppendLine("using System.Text;");
        sb.AppendLine("using System.IO;");
        sb.AppendLine("using System.Net.Http;");
        sb.AppendLine("using Conductor.Service.Script;"); // Fixed namespace
        sb.AppendLine("using Conductor.Service.Database;");
        sb.AppendLine("using Conductor.Types;");
        sb.AppendLine("using Conductor.Model;");
        sb.AppendLine();
        sb.AppendLine("namespace DynamicScript");
        sb.AppendLine("{");
        sb.AppendLine("    public class GeneratedScript : BaseScript");
        sb.AppendLine("    {");
        sb.AppendLine("        public override async Task<DataTable> ExecuteAsync<T>(T context, CancellationToken cancellationToken = default)");
        sb.AppendLine("        {");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine("                // Cast context to expected type");
        sb.AppendLine("                var scriptContext = context as ScriptContext;");
        sb.AppendLine("                if (scriptContext == null)");
        sb.AppendLine("                    throw new InvalidOperationException(\"Context must be of type ScriptContext\");");
        sb.AppendLine();
        sb.AppendLine("                // User script begins here");
        sb.AppendLine(IndentUserScript(userScript));
        sb.AppendLine("                // User script ends here");
        sb.AppendLine();
        sb.AppendLine("                // If user script doesn't return anything, return empty DataTable");
        sb.AppendLine("                return CreateDataTable();");
        sb.AppendLine("            }");
        sb.AppendLine("            catch (Exception ex)");
        sb.AppendLine("            {");
        sb.AppendLine("                throw new InvalidOperationException($\"Script execution error: {ex.Message}\", ex);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private string IndentUserScript(string script)
    {
        return string.Join("\n", script.Split('\n').Select(line => $"                {line}"));
    }

    private List<MetadataReference> BuildBaseReferences()
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(DataTable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(CancellationToken).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(HttpClient).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(StringBuilder).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.GetExecutingAssembly().Location),
        };

        // Add System.Runtime reference for .NET Core/5+
        try
        {
            var systemRuntime = Assembly.Load("System.Runtime");
            references.Add(MetadataReference.CreateFromFile(systemRuntime.Location));
        }
        catch
        {
            // Fallback if System.Runtime isn't available
        }

        return references;
    }

    private CSharpCompilation CreateCompilation(string sourceCode)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

        var references = new List<MetadataReference>(baseReference);

        // Add references to currently loaded assemblies (filtered)
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && 
                !string.IsNullOrEmpty(assembly.Location) &&
                !assembly.Location.Contains("roslyn", StringComparison.OrdinalIgnoreCase))
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
                .WithPlatform(Platform.AnyCpu)
                .WithAllowUnsafe(false)); // Security: disable unsafe code
    }

    private string ComputeHash(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes);
    }

    public void Dispose()
    {
        if (disposed) return;

        foreach (var context in loadContexts)
        {
            try
            {
                context.Unload();
            }
            catch
            {
                // Ignore unload errors during disposal
            }
        }

        compilationSemaphore.Dispose();
        scriptCache.Clear();
        disposed = true;
        GC.SuppressFinalize(this);
    }
}