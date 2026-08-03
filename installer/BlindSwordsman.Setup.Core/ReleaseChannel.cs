namespace BlindSwordsman.Setup.Core;

public enum ReleaseTrack
{
    Stable,
    Prerelease
}

public sealed record ReleaseAssetDescriptor(
    string Name,
    Uri Url,
    string Sha256,
    long Size);

public sealed record ReleaseChannelManifest(
    int SchemaVersion,
    SemanticVersion Version,
    string ReleaseTag,
    ReleaseTrack Track,
    SemanticVersion MinimumSetupVersion,
    ReleaseAssetDescriptor Payload,
    ReleaseAssetDescriptor Setup);
