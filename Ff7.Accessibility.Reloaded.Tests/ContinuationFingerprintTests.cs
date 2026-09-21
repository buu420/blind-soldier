using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The installed-script authorization for the continuation batch.
///
/// <para>Lives here rather than in the shared description tests because
/// <c>EchoSCompatibilityManifest</c> is compiled into the x86 runtime only - the x64 mod
/// project does not link it - so this is an x86-only concern and this file is deliberately
/// not linked into the x64 test project.</para>
/// </summary>
internal static class ContinuationFingerprintTests
{
    public static void Run()
    {
        foreach (var fieldId in FieldCutsceneContinuationDescriptions.CreateAll()
                     .Select(cue => cue.FieldId)
                     .Distinct()
                     .OrderBy(fieldId => fieldId))
        {
            ContinuationDescriptionTests.Equal(
                true,
                EchoSCompatibilityManifest.SupportsDescriptionField(fieldId),
                $"field {fieldId} must have an exact installed script identity");
        }

        // And nothing that already shipped lost its authorization.
        foreach (var fieldId in FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions()
                     .Select(cue => cue.FieldId)
                     .Distinct())
        {
            ContinuationDescriptionTests.Equal(
                true,
                EchoSCompatibilityManifest.SupportsDescriptionField(fieldId),
                $"catalog field {fieldId} must have an exact identity authorization entry");
        }
    }
}
