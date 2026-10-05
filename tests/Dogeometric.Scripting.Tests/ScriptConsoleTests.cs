using Dogeometric.Core.Modeling;
using Dogeometric.Scripting;

namespace Dogeometric.Scripting.Tests;

public class ScriptConsoleTests
{
    [Fact]
    public void Lines_share_their_variables_and_change_the_model_as_one_undo_step_each()
    {
        var document = new Document(new Model());
        var console = new ScriptConsole(document);

        Assert.Equal("=> 4\n", console.Run("var side = 2; side * 2").Output);
        var result = console.Run("AddFace(Pt(0, 0), Pt(side, 0), Pt(side, side), Pt(0, side)); puts(Entities.Faces.Count);");
        Assert.False(result.Failed);
        Assert.Equal("1\n", result.Output);
        Assert.Single(document.Model.Entities.Faces);

        Assert.True(document.Undo.Undo());
        Assert.Empty(document.Model.Entities.Faces);
    }

    [Fact]
    public void A_failing_line_reports_and_leaves_the_model_as_it_was()
    {
        var document = new Document(new Model());
        var console = new ScriptConsole(document);
        var result = console.Run("AddFace(Pt(0, 0), Pt(1, 0), Pt(1, 1)); throw new InvalidOperationException(\"stop\");");
        Assert.True(result.Failed);
        Assert.Contains("InvalidOperationException: stop", result.Output);
        Assert.Empty(document.Model.Entities.Faces);
        Assert.True(console.Run("nonsense(").Failed);
    }
}
