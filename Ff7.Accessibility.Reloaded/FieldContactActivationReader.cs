using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Whether the game has started a Contact target's Contact script, which is the only thing
/// that says a walk-into interaction has happened.
///
/// <para>Distance cannot say it. 00636C41 tests each step at probes a collision width ahead of
/// the party, and the step whose probe touches is refused, so the party's centre is the same on
/// the step before the bump and after it. When the touch comes, 0060C94D calls
/// 0060D29B(entity, 1, 2), which on accepting records script 2 at priority 1
/// (0x00CBF9E8[entity * 8 + 1]) and makes 1 the entity's current priority (0x00CC0B30[entity]);
/// the entity stays there until that script returns. This reads those two bytes for the target's
/// own entity through <see cref="FieldScriptControllerReader"/>'s two agreeing captures, never
/// through the globally current entity.</para>
/// </summary>
public sealed class FieldContactActivationReader
{
    private const int ContactPriority = 1;
    private const int ContactScript = 2;

    private readonly FieldScriptControllerReader controllers;

    public FieldContactActivationReader(ILegacyAddressSpace memory)
    {
        controllers = new FieldScriptControllerReader(memory);
    }

    public bool HasStarted(FieldNavigationTarget target) =>
        target.Activation == FieldNavigationActivation.Contact &&
        target.TriggerEntityId >= 0 &&
        controllers.TryReadRunningScript(target.FieldId, target.TriggerEntityId, out var priority, out var script) &&
        priority == ContactPriority &&
        script == ContactScript;
}
