using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;

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

    public void puts(object? value) => Output.AppendLine(value?.ToString() ?? "nil");

    public static Vec3 Pt(double x, double y, double z = 0) => new(x, y, z);

    /// <summary>A face through the points, in the collection being edited.</summary>
    public Face AddFace(params Vec3[] points) => Entities.AddFace(points);
}

/// <summary>The Ruby Console's engine (Extensions › Developer): each line is C# run against the open model, keeping earlier
/// lines' variables as Ruby's console does, and one undoable step.</summary>
public sealed class ScriptConsole(Document document)
{
    private readonly ScriptGlobals _globals = new(document);
    private ScriptState<object?>? _state;

    /// <summary>Folders to find an assembly's file in when it was loaded from memory (Godot loads the game's that way),
    /// since the compiler references assemblies by file.</summary>
    public static List<string> AssemblyFolders { get; } = [];

    private static ScriptOptions? _options;

    private static ScriptOptions Options => _options ??= ScriptOptions.Default
        .AddReferences(Reference(typeof(Model).Assembly), Reference(typeof(ScriptGlobals).Assembly), Reference(typeof(Enumerable).Assembly))
        .AddImports("System", "System.Linq", "System.Collections.Generic",
            "Dogeometric.Core.Geometry", "Dogeometric.Core.Modeling", "Dogeometric.Core.IO");

    // The script must see the assemblies already loaded, or a second copy of ScriptGlobals would not be the same type.
    private static InteractiveAssemblyLoader Loader()
    {
        var loader = new InteractiveAssemblyLoader();
        loader.RegisterDependency(typeof(ScriptGlobals).Assembly);
        loader.RegisterDependency(typeof(Model).Assembly);
        return loader;
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
            _state = _state == null
                ? CSharpScript.Create<object?>(code, Options, typeof(ScriptGlobals), Loader()).RunAsync(_globals).GetAwaiter().GetResult()
                : _state.ContinueWithAsync<object?>(code, Options).GetAwaiter().GetResult();
            document.Undo.Commit();
            if (_state.ReturnValue is { } value)
                _globals.Output.Append("=> ").AppendLine(value.ToString());
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
