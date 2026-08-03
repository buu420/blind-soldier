using BlindSwordsman.Setup.Core;

ReleaseManifestTests.Run();
Console.WriteLine("Blind Swordsman setup tests passed.");

static class ReleaseManifestTests
{
    public static void Run()
    {
        ParsesAValidPrereleaseManifest();
        ComparesSemanticVersionsWithoutLexicalErrors();
        RejectsMalformedAndUntrustedManifests();
        RejectsChannelMismatch();
    }

    private static void ParsesAValidPrereleaseManifest()
    {
        var manifest = ReleaseManifestParser.Parse(ValidManifest(), ReleaseTrack.Prerelease);

        Equal(1, manifest.SchemaVersion, "manifest schema");
        Equal("0.1.0-pre.1", manifest.Version.ToString(), "product version");
        Equal("v0.1.0-pre.1", manifest.ReleaseTag, "release tag");
        Equal("Blind-Swordsman-Runtime.zip", manifest.Payload.Name, "payload name");
        Equal(64, manifest.Payload.Sha256.Length, "payload hash length");
        Equal(ReleaseTrack.Prerelease, manifest.Track, "release track");
    }

    private static void ComparesSemanticVersionsWithoutLexicalErrors()
    {
        True(SemanticVersion.Parse("0.10.0") > SemanticVersion.Parse("0.9.9"), "numeric minor comparison");
        True(SemanticVersion.Parse("1.0.0") > SemanticVersion.Parse("1.0.0-rc.9"), "release after prerelease");
        True(SemanticVersion.Parse("1.0.0-rc.10") > SemanticVersion.Parse("1.0.0-rc.2"), "numeric prerelease comparison");
        Equal("1.2.3-pre.4+build.7", SemanticVersion.Parse("v1.2.3-pre.4+build.7").ToString(), "normalizes tag prefix");
    }

    private static void RejectsMalformedAndUntrustedManifests()
    {
        Throws<InvalidDataException>(() =>
            ReleaseManifestParser.Parse(ValidManifest().Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"), ReleaseTrack.Prerelease),
            "unknown schema");
        Throws<InvalidDataException>(() =>
            ReleaseManifestParser.Parse(ValidManifest().Replace("\"releaseTag\":", "\"unexpected\": true, \"releaseTag\":"), ReleaseTrack.Prerelease),
            "unknown root property");
        Throws<InvalidDataException>(() =>
            ReleaseManifestParser.Parse(ValidManifest().Replace("https://github.com/", "http://github.com/"), ReleaseTrack.Prerelease),
            "non-HTTPS asset URL");
        Throws<InvalidDataException>(() =>
            ReleaseManifestParser.Parse(ValidManifest().Replace("github.com/buu420", "example.com/buu420"), ReleaseTrack.Prerelease),
            "untrusted asset host");
        Throws<InvalidDataException>(() =>
            ReleaseManifestParser.Parse(ValidManifest().Replace(new string('A', 64), "BAD"), ReleaseTrack.Prerelease),
            "invalid SHA-256");
        Throws<InvalidDataException>(() =>
            ReleaseManifestParser.Parse(ValidManifest().Replace("Blind-Swordsman-Setup.exe", "Blind-Swordsman-Runtime.zip"), ReleaseTrack.Prerelease),
            "duplicate asset names");
        Throws<FormatException>(() => SemanticVersion.Parse("1.0"), "short semantic version");
        Throws<FormatException>(() => SemanticVersion.Parse("1.0.0-"), "empty prerelease");
    }

    private static void RejectsChannelMismatch()
    {
        Throws<InvalidDataException>(() =>
            ReleaseManifestParser.Parse(ValidManifest(), ReleaseTrack.Stable),
            "prerelease manifest on stable channel");
    }

    private static string ValidManifest() => $$"""
        {
          "schemaVersion": 1,
          "version": "0.1.0-pre.1",
          "releaseTag": "v0.1.0-pre.1",
          "track": "prerelease",
          "minimumSetupVersion": "0.1.0-pre.1",
          "payload": {
            "name": "Blind-Swordsman-Runtime.zip",
            "url": "https://github.com/buu420/blind-swordsman/releases/download/v0.1.0-pre.1/Blind-Swordsman-Runtime.zip",
            "sha256": "{{new string('A', 64)}}",
            "size": 1234
          },
          "setup": {
            "name": "Blind-Swordsman-Setup.exe",
            "url": "https://github.com/buu420/blind-swordsman/releases/download/v0.1.0-pre.1/Blind-Swordsman-Setup.exe",
            "sha256": "{{new string('B', 64)}}",
            "size": 5678
          }
        }
        """;

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void True(bool value, string label)
    {
        if (!value)
        {
            throw new InvalidOperationException($"{label}: expected true.");
        }
    }

    private static void Throws<TException>(Action action, string label)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"{label}: expected {typeof(TException).Name}, got {exception.GetType().Name}.", exception);
        }

        throw new InvalidOperationException($"{label}: expected {typeof(TException).Name}, but no exception was thrown.");
    }
}
