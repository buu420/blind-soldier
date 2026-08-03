namespace Ff7.Accessibility.Steam2026X64.Runtime.Movies;

/// <summary>
/// Maps the one live-verified Steam 2026 virtual opening-movie identity to
/// the already validated physical opening path consumed by the shared
/// lifecycle observer. No basename, substring, traversal, or alternate
/// virtual-root match is accepted.
/// </summary>
internal sealed class Steam2026OpeningMoviePathIdentity
{
    internal const string VerifiedVirtualPath = "0://data/movies/opening.avi";

    private readonly string expectedPhysicalPath;

    internal Steam2026OpeningMoviePathIdentity(string expectedPhysicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPhysicalPath);
        if (!Path.IsPathFullyQualified(expectedPhysicalPath))
        {
            throw new ArgumentException(
                "The physical opening movie path must be fully qualified.",
                nameof(expectedPhysicalPath));
        }

        this.expectedPhysicalPath = Path.GetFullPath(expectedPhysicalPath);
    }

    internal bool TryMapForObserver(string? observedPath, out string mappedPath)
    {
        mappedPath = string.Empty;
        if (string.IsNullOrEmpty(observedPath))
        {
            return false;
        }

        if (!string.Equals(
                observedPath,
                VerifiedVirtualPath,
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                observedPath,
                expectedPhysicalPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        mappedPath = expectedPhysicalPath;
        return true;
    }
}
