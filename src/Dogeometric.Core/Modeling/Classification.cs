namespace Dogeometric.Core.Modeling;

/// <summary>
/// SketchUp's Classifier: an IFC type on a component definition (or group), which the IFC export writes the element as.
/// </summary>
public static class Classification
{
    public sealed record IfcType(string Name, string Category, int Attributes);

    /// <summary>The IFC 4 element types offered, by category, with each entity's attribute count in the schema.</summary>
    public static readonly IReadOnlyList<IfcType> Types =
    [
        .. Category("Building", 9, "IfcBeam", "IfcBuildingElementProxy", "IfcChimney", "IfcColumn", "IfcCovering", "IfcCurtainWall",
            "IfcFooting", "IfcMember", "IfcPlate", "IfcRailing", "IfcRamp", "IfcRampFlight", "IfcRoof", "IfcShadingDevice", "IfcSlab",
            "IfcStair", "IfcWall", "IfcBuildingElementPart"),
        new("IfcDoor", "Building", 13), new("IfcWindow", "Building", 13), new("IfcStairFlight", "Building", 13), new("IfcPile", "Building", 10),
        .. Category("Furnishing", 9, "IfcFurniture", "IfcSystemFurnitureElement"),
        .. Category("Electrical", 9, "IfcAlarm", "IfcAudioVisualAppliance", "IfcCableCarrierSegment", "IfcCableFitting", "IfcCableSegment",
            "IfcCommunicationsAppliance", "IfcController", "IfcElectricAppliance", "IfcElectricDistributionBoard", "IfcElectricGenerator",
            "IfcElectricMotor", "IfcJunctionBox", "IfcLamp", "IfcLightFixture", "IfcOutlet", "IfcProtectiveDevice", "IfcSensor",
            "IfcSwitchingDevice", "IfcTransformer"),
        .. Category("Mechanical and plumbing", 9, "IfcActuator", "IfcAirTerminal", "IfcDuctFitting", "IfcDuctSegment", "IfcFan",
            "IfcPipeFitting", "IfcPipeSegment", "IfcPump", "IfcSanitaryTerminal", "IfcTank", "IfcValve"),
        .. Category("Fixings", 9, "IfcDiscreteAccessory", "IfcFastener"),
        new("IfcMechanicalFastener", "Fixings", 11),
        new("IfcElementAssembly", "Other", 10),
        .. Category("Other", 9, "IfcGeographicElement", "IfcTransportElement"),
        new("IfcVirtualElement", "Other", 8),
    ];

    private static IEnumerable<IfcType> Category(string category, int attributes, params string[] names) =>
        names.Select(n => new IfcType(n, category, attributes));

    public static IfcType? Find(string name) => Types.FirstOrDefault(t => t.Name == name);

    /// <summary>Classifies what <paramref name="instance"/> is a copy of, with an IFC type or, given its
    /// <paramref name="schema"/>, a type of an imported schema; <paramref name="unique"/> (Shift) first makes it a
    /// definition of its own, so the other copies keep theirs.</summary>
    public static void Apply(Model model, ComponentInstance instance, string type, bool unique = false, string? schema = null)
    {
        if (unique && !instance.IsGroup)
            Grouping.MakeUnique(model, instance);
        if (schema == null || IsIfc(schema))
            instance.Definition.IfcType = type;
        else
            instance.Definition.SchemaTypes[schema] = type;
    }

    /// <summary>IFC schemas ("IFC 2x3", "IFC 4") share the built-in IFC types the IFC export writes.</summary>
    public static bool IsIfc(string schema) => schema.StartsWith("IFC", StringComparison.OrdinalIgnoreCase);

    /// <summary>Ctrl with the Classifier: the definition loses all its types.</summary>
    public static void Erase(ComponentDefinition definition)
    {
        definition.IfcType = "";
        definition.SchemaTypes.Clear();
    }
}

/// <summary>A classification schema imported from a .skc file: its types and the attributes each one carries.</summary>
public sealed class ClassificationSchema
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<(string Type, List<string> Attributes)> Types { get; } = [];
}
