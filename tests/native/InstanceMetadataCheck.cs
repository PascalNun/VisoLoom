using System.Text.Json;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using VisoLoom.Core;
using VisoLoom.Plugin;

namespace VisoLoom.NativeChecks;

// The library plan's first verification fixture: it pins how block metadata
// behaves before any field names are fixed. Definition-level and placement-level
// Attribute User Text are written, copied, edited, saved and reopened on isolated
// headless documents, and the same placements are resolved through the real
// extraction path. The active Rhino document and its views are never touched.
public static class InstanceMetadataCheck
{
    private const string Kind = "VisoLoom.FixtureKind";
    private const string Revision = "VisoLoom.FixtureRevision";
    private const string Variation = "VisoLoom.FixtureVariation";

    public static void Run(string report)
    {
        string path = Path.Combine(Path.GetDirectoryName(report)!, "instance-metadata.3dm");
        // The fixture documents set no per-view overrides, so an unclaimed viewport id
        // reads every layer's default visibility.
        Guid view = Guid.NewGuid();
        using var doc = RhinoDoc.CreateHeadless(null) ?? throw new InvalidOperationException("Could not create the fixture document.");

        var crownAttributes = new ObjectAttributes();
        crownAttributes.SetUserString(Kind, "crown");
        int crownIndex = doc.InstanceDefinitions.Add("Fixture Crown", "Library fixture, crown", Point3d.Origin,
            [new Sphere(new Point3d(1, 1, 1), 0.5).ToBrep()], [crownAttributes]);
        var crown = doc.InstanceDefinitions[crownIndex] ?? throw new InvalidOperationException("Rhino did not create the crown definition.");

        // The tree definition carries its own content metadata and nests the crown once.
        var nested = Rhino.Geometry.Transform.Translation(2, 0, 5);
        var treeAttributes = new ObjectAttributes();
        treeAttributes.SetUserString(Kind, "tree");
        treeAttributes.SetUserString(Revision, "2");
        int treeIndex = doc.InstanceDefinitions.Add("Fixture Tree", "Library fixture, tree", Point3d.Origin,
            [new BoundingBox(new Point3d(-1, -1, 0), new Point3d(1, 1, 4)).ToBrep(), new InstanceReferenceGeometry(crown.Id, nested)],
            [treeAttributes, new ObjectAttributes()]);
        var tree = doc.InstanceDefinitions[treeIndex] ?? throw new InvalidOperationException("Rhino did not create the tree definition.");

        // Four visible placements: plain, uniform scale with a rotation and metadata,
        // anisotropic scale with different metadata, and one duplicated from the second.
        Guid plain = doc.Objects.AddInstanceObject(tree.Index, Rhino.Geometry.Transform.Translation(20, 10, 0));
        var autumn = new ObjectAttributes();
        autumn.SetUserString(Variation, "autumn");
        var scaledAndRotated = Rhino.Geometry.Transform.Scale(Point3d.Origin, 2) *
            Rhino.Geometry.Transform.Rotation(0.5, Vector3d.ZAxis, Point3d.Origin) * Rhino.Geometry.Transform.Translation(50, 10, 0);
        Guid decorated = doc.Objects.AddInstanceObject(tree.Index, scaledAndRotated, autumn);
        var summer = new ObjectAttributes();
        summer.SetUserString(Variation, "summer");
        var stretched = Rhino.Geometry.Transform.Scale(Plane.WorldXY, 2, 1, 1) * Rhino.Geometry.Transform.Translation(80, 10, 0);
        Guid neighbour = doc.Objects.AddInstanceObject(tree.Index, stretched, summer);
        var decoratedObject = (InstanceObject)doc.Objects.FindId(decorated)!;
        Guid copy = doc.Objects.AddInstanceObject(tree.Index, decoratedObject.InstanceXform, decoratedObject.Attributes.Duplicate());

        // A placement on an invisible layer is not visible geometry and must not resolve.
        int hiddenLayer = doc.Layers.Add("Fixture Hidden", System.Drawing.Color.Gray);
        var hiddenAttributes = new ObjectAttributes { LayerIndex = hiddenLayer };
        hiddenAttributes.SetUserString(Variation, "hidden");
        Guid hidden = doc.Objects.AddInstanceObject(tree.Index, Rhino.Geometry.Transform.Translation(110, 10, 0), hiddenAttributes);
        doc.Layers[hiddenLayer].IsVisible = false;

        var rows = SceneBuilder.ResolveInstances(doc, view);
        bool allPlacementsResolve = new[] { plain, decorated, neighbour, copy }.All(id => rows.Any(row => row.InstanceId == id && row.Path.Count == 0));
        bool nestedPlacementsCompose = rows.Count == 8 && rows.Count(row => row.Path.Count == 1 &&
            row.DefinitionId == crown.Id && row.Path[0] == decorated) == 1;
        bool hiddenPlacementExcluded = rows.All(row => row.InstanceId != hidden);

        // The resolved matrix must agree with Rhino's own composition of the same transforms.
        var probe = new Point3d(0.3, -0.7, 1.1);
        var nestedRow = rows.Single(row => row.Path.Count == 1 && row.Path[0] == decorated);
        var viaRhino = (scaledAndRotated * nested) * probe;
        var viaRow = Apply(nestedRow.Xform, probe);
        bool worldTransformAgreesWithRhinoMath = viaRhino.DistanceTo(viaRow) < 1e-9;
        bool scaleReadsBasisLengths = Math.Abs(nestedRow.Scale.X - 2) < 1e-9 && Math.Abs(nestedRow.Scale.Y - 2) < 1e-9 &&
            Math.Abs(nestedRow.Scale.Z - 2) < 1e-9 &&
            rows.Single(row => row.InstanceId == neighbour).Scale is { X: > 1.99 and < 2.01, Y: > 0.99 and < 1.01, Z: > 0.99 and < 1.01 };

        // Definition metadata is shared once through the definition; placement metadata stays per placement.
        var treeContents = tree.GetObjects() ?? [];
        bool definitionMetadataSharesAcrossPlacements =
            treeContents.Count(contents => contents.Attributes.GetUserString(Kind) == "tree") == 1 &&
            rows.Count(row => row.DefinitionId == tree.Id) == 4;
        bool placementMetadataIndependent =
            doc.Objects.FindId(decorated)!.Attributes.GetUserString(Variation) == "autumn" &&
            doc.Objects.FindId(neighbour)!.Attributes.GetUserString(Variation) == "summer" &&
            doc.Objects.FindId(plain)!.Attributes.GetUserString(Variation) is null;
        bool copyInheritsDefinitionNotSiblings = doc.Objects.FindId(copy)!.Attributes.GetUserString(Variation) == "autumn" &&
            doc.Objects.FindId(copy)!.Attributes.GetUserString(Variation) != "summer";

        // A definition edit keeps its identity and, when the editor carries the content
        // attributes over, its metadata — otherwise recorded placements would detach.
        var contents = tree.GetObjects()!;
        bool definitionIdentityAndMetadataSurviveEdits = doc.InstanceDefinitions.ModifyGeometry(tree.Index,
            [.. contents.Select(o => o.Geometry.Duplicate())], [.. contents.Select(o => o.Attributes.Duplicate())]) &&
            doc.InstanceDefinitions.Find(tree.Id, true) is { } edited && edited.Id == tree.Id &&
            (edited.GetObjects() ?? []).Any(o => o.Attributes.GetUserString(Kind) == "tree") &&
            SceneBuilder.ResolveInstances(doc, view).Single(row => row.Path.Count == 1 && row.Path[0] == decorated).InstanceId == nestedRow.InstanceId;

        if (!doc.SaveAs(path))
            throw new InvalidOperationException("Could not save the fixture document.");
        string before = JsonSerializer.Serialize(rows);
        using var reopened = RhinoDoc.OpenHeadless(path) ?? throw new InvalidOperationException("Could not reopen the fixture document.");
        bool saveReopenKeepsResolution = JsonSerializer.Serialize(SceneBuilder.ResolveInstances(reopened, view)) == before;
        bool saveReopenKeepsDefinitionMetadata = reopened.InstanceDefinitions.Find(tree.Id, true) is { } reopenedTree &&
            (reopenedTree.GetObjects() ?? []).Any(o => o.Attributes.GetUserString(Kind) == "tree");
        bool saveReopenKeepsPlacementMetadata =
            reopened.Objects.FindId(decorated) is { } reopenedDecorated && reopenedDecorated.Attributes.GetUserString(Variation) == "autumn" &&
            reopened.Objects.FindId(neighbour) is { } reopenedNeighbour && reopenedNeighbour.Attributes.GetUserString(Variation) == "summer";

        File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            allPlacementsResolve, nestedPlacementsCompose, hiddenPlacementExcluded,
            worldTransformAgreesWithRhinoMath, scaleReadsBasisLengths, definitionMetadataSharesAcrossPlacements,
            placementMetadataIndependent, copyInheritsDefinitionNotSiblings, definitionIdentityAndMetadataSurviveEdits,
            saveReopenKeepsResolution, saveReopenKeepsDefinitionMetadata, saveReopenKeepsPlacementMetadata
        }));
    }

    /// <summary>The documented wire convention: Rhino transforms act on row vectors,
    /// so the fourth row carries the translation.</summary>
    private static Point3d Apply(IReadOnlyList<double> m, Point3d p) => new(
        p.X * m[0] + p.Y * m[4] + p.Z * m[8] + m[12],
        p.X * m[1] + p.Y * m[5] + p.Z * m[9] + m[13],
        p.X * m[2] + p.Y * m[6] + p.Z * m[10] + m[14]);
}
