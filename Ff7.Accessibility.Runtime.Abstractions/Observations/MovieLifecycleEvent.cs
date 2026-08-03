namespace Ff7.Accessibility.Runtime.Abstractions;

public enum MovieLifecycleKind
{
    Started,
    Stopped,
    Skipped
}

public sealed record MovieLifecycleEvent(
    DateTime TimestampUtc,
    MovieLifecycleKind Kind,
    string NativeMovieKey) : RuntimeEvent(TimestampUtc);
