namespace Ff7.Accessibility.Reloaded;

public static class OpeningMoviePathResolver
{
    public static string Resolve(string gameRoot, bool ffnxLoaded, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        var dataMovie = Path.Combine(gameRoot, "data", "movies", "opening.avi");
        if (!ffnxLoaded)
        {
            return dataMovie;
        }

        var overrideMovie = Path.Combine(gameRoot, "override", "movies", "opening.avi");
        return fileExists(overrideMovie) ? overrideMovie : dataMovie;
    }
}
