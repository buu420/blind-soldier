namespace Ff7.Accessibility.Runtime.Abstractions;

public sealed record RuntimeIdentity(
    string RuntimeId,
    string ExecutablePath,
    string Sha256,
    bool Is64Bit,
    string FileVersion);
