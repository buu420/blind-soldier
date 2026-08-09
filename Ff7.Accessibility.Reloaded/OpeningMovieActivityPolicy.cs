namespace Ff7.Accessibility.Reloaded;

public enum OpeningMovieActivitySignal
{
    None,
    FileHandle,
    NativeFieldMovieState
}

public readonly record struct OpeningMovieActivity(
    bool IsActive,
    OpeningMovieActivitySignal Signal);

public static class OpeningMovieActivityPolicy
{
    public static OpeningMovieActivity Resolve(
        bool fileHandleActive,
        bool nativeStateReadable,
        byte nativeModule,
        ushort nativeFieldId,
        ushort nativeMovieActive)
    {
        if (fileHandleActive)
        {
            return new(true, OpeningMovieActivitySignal.FileHandle);
        }

        if (nativeStateReadable &&
            nativeModule == FieldPositionReader.FieldModule &&
            nativeFieldId == DeferredZoneSpeechTracker.OpeningFieldId &&
            nativeMovieActive != 0)
        {
            return new(true, OpeningMovieActivitySignal.NativeFieldMovieState);
        }

        return new(false, OpeningMovieActivitySignal.None);
    }
}
