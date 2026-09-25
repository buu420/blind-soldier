using Ff7.Accessibility.Reloaded;

namespace WholeGameStateAudit;

/// <summary>
/// The label the shipping NPC reader would speak for a catalogued NPC, or empty where it
/// drops the NPC for want of one. The same harness FieldInteractionAudit uses: native
/// memory is answered so that every position-independent gate passes (loaded model that
/// is not the player's, visible, Talk enabled, any line live), so what survives is exactly
/// what ResolveLabel decided.
/// </summary>
internal static class ShippingLabels
{
    public static string Resolve(int fieldId, FieldScriptNpcDefinition npc, FlevelFieldTextResolver textResolver)
    {
        const int eventTable = 0x02404000;
        var modelId = (byte)Math.Min(npc.EntityId + 1, 254);
        var reader = new FieldNavigationNpcReader(
            address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? eventTable : 0,
            _ => 48,
            address =>
            {
                if (address == FieldPositionReader.AddressFieldNumModels)
                {
                    return 255;
                }

                if (address >= FieldNavigationObjectReader.AddressFieldModelIdArray &&
                    address < FieldNavigationObjectReader.AddressFieldModelIdArray + 256)
                {
                    return address == FieldNavigationObjectReader.AddressFieldModelIdArray + npc.EntityId
                        ? modelId
                        : (byte)0xFF;
                }

                var eventAddress = eventTable + modelId * FieldNavigationObjectReader.FieldEventDataStride;
                return address == eventAddress + FieldNavigationObjectReader.VisibilityOffset ? (byte)1 : (byte)0;
            },
            (field, dialogId) => textResolver.ReadMessageLinesById(field, dialogId),
            _ => [npc],
            null,
            _ => true);

        try
        {
            var targets = reader.ReadTargets(new FieldPositionSnapshot(1, fieldId, 0, 0, 0, 0, 0, 0));
            return targets.Count > 0 ? targets[0].Label : string.Empty;
        }
        catch (Exception exception)
        {
            return $"<label failed: {exception.GetType().Name}>";
        }
    }
}
