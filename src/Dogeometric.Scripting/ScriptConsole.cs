using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Dogeometric.Scripting;

/// <summary>What a console line can reach: the model, the collection being edited, the selection and a few helpers in
/// the spirit of SketchUp's Ruby API (<c>puts</c>, <c>Pt</c>, <c>AddFace</c>).</summary>
public sealed class ScriptGlobals
{
    internal ScriptGlobals(Document document) => Document = document;

    internal StringBuilder Output { get; } = new();

    public Document Document { get; }
    public Model Model => Document.Model;
    public Entities Entities => Document.Context.Entities;
    public Selection Selection => Document.Selection;

    public void puts(object? value) => Output.Append(value?.ToString() ?? "nil").Append('\n');

    public static Vec3 Pt(double x, double y, double z = 0) => new(x, y, z);

    /// <summary>A face through the points, in the collection being edited.</summary>
    public Face AddFace(params Vec3[] points) => Entities.AddFace(points);
}

/// <summary>The Ruby Console's engine (Extensions › Developer): each line is C# run against the open model, keeping earlier
/// lines' variables as Ruby's console does, and one undoable step.</summary>
public sealed class ScriptConsole(Document document)
{
    private readonly ScriptGlobals _globals = new(document);
    private Script<object?>? _last;
    private readonly List<object?> _submissions = [];

    /// <summary>Folders to find an assembly's file in when it was loaded from memory (Godot loads the game's that way),
    /// since the compiler references assemblies by file.</summary>
    public static List<string> AssemblyFolders { get; } = [];

    private static ScriptOptions? _options;

    private static ScriptOptions Options => _options ??= ScriptOptions.Default
        .AddReferences(Reference(typeof(Model).Assembly), Reference(typeof(ScriptGlobals).Assembly), Reference(typeof(Enumerable).Assembly))
        .AddImports("System", "System.Linq", "System.Collections.Generic",
            "Dogeometric.Core.Geometry", "Dogeometric.Core.Modeling", "Dogeometric.Core.IO");

    /// <summary>
    /// Compiles the submission and runs it in the load context the console itself lives in (Godot's for the game), so
    /// its ScriptGlobals is the one the script sees; Roslyn's own loader would bring in a second copy.
    /// </summary>
    private object? Execute(Script<object?> script)
    {
        var compilation = script.GetCompilation();
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        if (!emitted.Success)
            throw new CompilationErrorException("The line does not compile", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray());
        image.Position = 0;
        var context = AssemblyLoadContext.GetLoadContext(typeof(ScriptGlobals).Assembly) ?? AssemblyLoadContext.Default;
        var assembly = context.LoadFromStream(image);
        var type = assembly.GetType(compilation.ScriptClass!.MetadataName, throwOnError: true)!;
        var factory = type.GetMethod("<Factory>", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        // Slot 0 holds the globals, then one slot per earlier submission.
        if (_submissions.Count == 0)
            _submissions.Add(_globals);
        _submissions.Add(null);
        var states = _submissions.ToArray();
        object? result;
        try
        {
            result = ((Task<object?>)factory.Invoke(null, [states])!).GetAwaiter().GetResult();
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            // A failed line is not part of the chain: the next one takes its slot.
            _submissions.RemoveAt(_submissions.Count - 1);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
        for (var i = 0; i < states.Length; i++)
            _submissions[i] = states[i];
        return result;
    }

    private static MetadataReference Reference(System.Reflection.Assembly assembly)
    {
        if (!string.IsNullOrEmpty(assembly.Location))
            return MetadataReference.CreateFromFile(assembly.Location);
        var file = assembly.GetName().Name + ".dll";
        var path = AssemblyFolders.Append(AppContext.BaseDirectory).Select(f => Path.Combine(f, file)).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"{file} was loaded without a file and is not in the known folders");
        return MetadataReference.CreateFromFile(path);
    }

    public sealed record Result(string Output, bool Failed);

    /// <summary>Runs one line (or block) and returns what it printed and its value, as the console shows it.</summary>
    public Result Run(string code)
    {
        _globals.Output.Clear();
        document.Undo.Begin("Ruby Console", document.Context.Entities);
        try
        {
            var script = _last == null
                ? CSharpScript.Create<object?>(code, Options, typeof(ScriptGlobals))
                : _last.ContinueWith<object?>(code, Options);
            var value = Execute(script);
            _last = script;
            document.Undo.Commit();
            if (value != null)
                _globals.Output.Append("=> ").Append(value.ToString()).Append('\n');
            return new Result(_globals.Output.ToString(), false);
        }
        catch (CompilationErrorException ex)
        {
            document.Undo.Abort();
            return new Result(_globals.Output + string.Join("\n", ex.Diagnostics.Select(d => d.ToString())) + "\n", true);
        }
        catch (Exception ex)
        {
            document.Undo.Abort();
            var inner = ex.InnerException is { } cause ? $" ({cause.GetType().Name}: {cause.Message})" : "";
            return new Result(_globals.Output + $"{ex.GetType().Name}: {ex.Message}{inner}\n", true);
        }
    }
}
