namespace VisoLoom.Core;

public enum MappingSource { Inferred, OfficeDefault, ProjectDefault, Layer, Material, Object }
public sealed record DescriptionMapping(Guid SourceId, MappingSource Source, string Description, int LayerDistance = 0);
public sealed record MappingResolution(string? Description, MappingSource? Source, bool HasConflict);

public static class MaterialMappingResolver
{
    // Identity is a stable source GUID; callers never use mutable table indices or names as keys.
    public static MappingResolution Resolve(IEnumerable<DescriptionMapping> applicable)
    {
        var ordered = applicable.OrderByDescending(x => x.Source).ThenBy(x => x.LayerDistance).ToArray();
        if (ordered.Length == 0) return new(null, null, false);
        var first = ordered[0];
        var peers = ordered.Where(x => x.Source == first.Source && x.LayerDistance == first.LayerDistance);
        if (peers.Select(x => x.Description).Distinct(StringComparer.Ordinal).Count() > 1) return new(null, first.Source, true);
        return new(first.Description, first.Source, false);
    }
}
