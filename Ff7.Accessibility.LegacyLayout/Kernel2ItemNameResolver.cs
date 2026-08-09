namespace Ff7.Accessibility.Reloaded;

public sealed class Kernel2ItemNameResolver
{
    private const int ItemCountValue = 128;
    private readonly Kernel2TextDatabase database;

    private Kernel2ItemNameResolver(Kernel2TextDatabase database)
    {
        this.database = database;
    }

    public int ItemCount => ItemCountValue;

    public string? ResolveName(int itemId) =>
        itemId is >= 0 and < ItemCountValue ? database.ResolveItemName(itemId) : null;

    public static Kernel2ItemNameResolver? TryCreate(string gameRootDirectory, Action<string>? log = null)
    {
        var language = Ff7GameLanguageDetector.Detect(gameRootDirectory, log: log);
        return TryCreate(language, log);
    }

    public static Kernel2ItemNameResolver? TryCreate(
        Ff7GameLanguageContext language,
        Action<string>? log = null)
    {
        var database = Kernel2TextDatabase.TryCreate(language, log);
        if (database is null)
        {
            return null;
        }

        log?.Invoke($"kernel2 item names loaded for {language.DisplayName}; count={ItemCountValue}");
        return new Kernel2ItemNameResolver(database);
    }

    internal static Kernel2ItemNameResolver? TryCreateFromDecodedKernel2(byte[] decoded) =>
        TryCreateFromDecodedKernel2(decoded, Ff7GameLanguages.Get(Ff7GameLanguage.English));

    internal static Kernel2ItemNameResolver? TryCreateFromDecodedKernel2(
        byte[] decoded,
        Ff7GameLanguageDescriptor language)
    {
        var database = Kernel2TextDatabase.TryCreateFromDecodedKernel2(decoded, language);
        return database is null ? null : new Kernel2ItemNameResolver(database);
    }
}
