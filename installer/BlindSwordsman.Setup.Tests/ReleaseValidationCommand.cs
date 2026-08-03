using BlindSwordsman.Setup.Core;

static class ReleaseValidationCommand
{
    public static void Run(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 4)
        {
            throw new ArgumentException("validate-release requires manifest, payload, setup, and track arguments.");
        }
        var track = arguments[3] switch
        {
            "stable" => ReleaseTrack.Stable,
            "prerelease" => ReleaseTrack.Prerelease,
            _ => throw new ArgumentException("validate-release received an unknown release track.")
        };
        var manifestPath = Path.GetFullPath(arguments[0]);
        var payloadPath = Path.GetFullPath(arguments[1]);
        var setupPath = Path.GetFullPath(arguments[2]);
        var manifest = ReleaseManifestParser.Parse(File.ReadAllText(manifestPath), track);
        ValidateAsset(manifest.Payload, payloadPath);
        ValidateAsset(manifest.Setup, setupPath);

        var temporary = Path.Combine(Path.GetTempPath(), "blind-swordsman-release-validation-" + Guid.NewGuid().ToString("N"));
        try
        {
            SafeZipExtractor.ExtractAndValidate(payloadPath, temporary);
            ReleasePayloadLayoutValidator.Validate(temporary);
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
        Console.WriteLine($"Validated Blind Swordsman release {manifest.ReleaseTag}.");
    }

    private static void ValidateAsset(ReleaseAssetDescriptor descriptor, string path)
    {
        var item = new FileInfo(path);
        if (!item.Exists || !string.Equals(item.Name, descriptor.Name, StringComparison.Ordinal) || item.Length != descriptor.Size)
        {
            throw new InvalidDataException($"Release asset metadata does not match {descriptor.Name}.");
        }
        var hash = HashVerifier.ComputeSha256Async(path, CancellationToken.None).GetAwaiter().GetResult();
        if (!HashVerifier.FixedTimeEquals(descriptor.Sha256, hash))
        {
            throw new InvalidDataException($"Release asset hash does not match {descriptor.Name}.");
        }
    }
}
