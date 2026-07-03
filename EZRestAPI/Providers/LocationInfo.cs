namespace EZRestAPI.Providers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// A value-equatable snapshot of a source location, safe to carry through
/// incremental pipeline models (unlike <see cref="Location"/> itself, which
/// holds a syntax tree reference and would defeat caching).
/// </summary>
public record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public static LocationInfo? From(Location? location)
    {
        if (location is null || location.SourceTree is null)
        {
            return null;
        }

        return new LocationInfo(
            location.SourceTree.FilePath,
            location.SourceSpan,
            location.GetLineSpan().Span
        );
    }

    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);
}
