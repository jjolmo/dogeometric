using Dogeometric.Core.Units;

namespace Dogeometric.App.UI;

/// <summary>Lengths as the open model shows and reads them (Model Info › Units), for the Measurements box and panels.</summary>
public static class Measure
{
    /// <summary>The open model, whose Model Info › Units apply (set once by the document controller).</summary>
    public static Func<Core.Modeling.Model?> Model { get; set; } = () => null;

    public static UnitSettings Settings => Model()?.UnitSettings ?? new UnitSettings();

    public static string Show(double millimeters) => Length.Format(millimeters, Settings);

    public static bool Read(string text, out double millimeters) => Length.TryParse(text, Settings.InputUnit, out millimeters);
}
