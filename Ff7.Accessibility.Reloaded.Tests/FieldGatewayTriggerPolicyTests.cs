using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

/// <summary>
/// The fields whose gateway table describes doors the game will never open.
///
/// <para>FFVII's <c>MPJPO</c> opcode, 0xD2 with a one-byte operand, switches every static
/// gateway trigger off at once; the field movement handler then skips the gateway crossing
/// check entirely. A field that calls it in its Director's Init and never re-enables has a
/// full gateway table and no working doors in it.</para>
///
/// <para>This is not theoretical. In the 2026-09-08 recording the player selected "Exit to
/// Ticket Office" in the Chocobo Square racing room and auto walk drove at it for
/// ninety-nine consecutive samples - crossing the gateway's own exit line every time, since
/// the party oscillated between (978,-169) and (978,-153) either side of the line from
/// (912,-272) to (1039,-49) - without the field ever changing. The player had to take over
/// by hand. <c>crcin_2</c>'s Director calls MPJPO at byte 191 of its Init.</para>
///
/// <para>The first case re-derives the whole list from the installed scripts rather than
/// trusting the constant: a hand-maintained list of game facts is only safe if something
/// checks it against the game.</para>
/// </summary>
internal static class FieldGatewayTriggerPolicyTests
{
    private const byte GatewayTriggerToggleOpcode = 0xD2;

    // The installed field table runs to 766; the sweep stops where the data does.
    private const int MaximumFieldId = 766;

    internal static void Run()
    {
        ThePolicyMatchesTheInstalledScripts();
        AFieldThatDisablesItsGatewaysOffersNoneOfThem();
        AnOrdinaryFieldStillOffersItsGateways();
    }

    private static void ThePolicyMatchesTheInstalledScripts()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                "FF7_ACCESSIBILITY_DATA_ROOT must point at the installed game data.");
        }

        var catalog = new FieldScriptNavigationCatalog(root);
        var derived = new List<int>();
        foreach (var fieldId in FieldGatewayTriggerPolicy.SuppressedFields)
        {
            var scripts = catalog.ReadAllScriptOpcodes(fieldId);
            if (scripts.Count == 0)
            {
                throw new InvalidOperationException(
                    $"field {fieldId} could not be read from the installed data.");
            }

            // The Director is entity 0, and its Init is the first script it owns. A field
            // that re-enables anywhere is left alone: its gateways do work, when the script
            // says they do.
            var disablesInInit = scripts.Any(script =>
                script.EntityId == 0 &&
                script.ScriptId == 0 &&
                script.Opcodes.Any(IsDisableAllGateways));
            var reEnablesSomewhere = scripts.Any(script =>
                script.Opcodes.Any(IsEnableAllGateways));
            Equal(true, disablesInInit,
                $"field {fieldId} is on the suppression list, so its Director's Init must " +
                "really call MPJPO with a disabling operand");
            Equal(false, reEnablesSomewhere,
                $"field {fieldId} must not re-enable its gateways anywhere, or suppressing " +
                "them for the whole visit would be wrong");

            // And the disable has to actually run. A branch that jumps past it leaves a
            // path out of Init with the gateways still live, which is how junair and
            // itown12 got onto an earlier version of this list and would have had their
            // working doors hidden.
            var director = scripts.Single(script => script.EntityId == 0 && script.ScriptId == 0);
            Equal(string.Empty, DescribeBypasses(director),
                $"field {fieldId}'s gateway disable must run on every path out of Init");
            derived.Add(fieldId);
        }

        Equal(FieldGatewayTriggerPolicy.SuppressedFields.Count, derived.Count,
            "every suppressed field must be justified by the installed scripts");

        // The other direction, which is the one that matters for a hand-written list: no
        // field anywhere in the installed data may match the rule and be missing from it.
        // Only fields that actually have a gateway can be affected, which bounds the sweep.
        var missing = new List<int>();
        for (var fieldId = 0; fieldId <= MaximumFieldId; fieldId++)
        {
            if (!FieldGatewayTriggerPolicy.AreGatewayTriggersUsable(fieldId))
            {
                continue;
            }

            var scripts = catalog.ReadAllScriptOpcodes(fieldId);
            if (scripts.Count == 0)
            {
                continue;
            }

            var disables = scripts.Any(script =>
                script.EntityId == 0 && script.ScriptId == 0 &&
                script.Opcodes.Any(IsDisableAllGateways));
            var reEnables = scripts.Any(script => script.Opcodes.Any(IsEnableAllGateways));
            if (disables && !reEnables)
            {
                missing.Add(fieldId);
            }
        }

        // What is left over must be exactly the fields that disable their gateways only
        // *after* a GameMoment test, which is a different thing: their doors do work,
        // earlier in the story, and hiding them would take away real exits. 270 niv_w,
        // 271 nvmin1_1, 273 nivinn_1 and 286 niv_ti1 all disable in the Director's Main
        // behind "moment greater than 384", and 493 ghotin_4 behind "moment is 601".
        // 384 junair and 713 itown12 belong here too, and this is why: both reach their
        // MPJPO only inside a GameMoment branch, so their doors work the rest of the time.
        // An earlier version of the policy suppressed them and would have hidden real exits.
        Equal(true, FieldGatewayTriggerPolicy.AreGatewayTriggersUsable(384),
            "junair disables its gateways only when the moment is 1299 and a flag is clear");
        Equal(true, FieldGatewayTriggerPolicy.AreGatewayTriggersUsable(713),
            "itown12 disables its gateways only when the moment is 1100");

        Equal("270,271,273,286,384,493,713", string.Join(",", missing),
            "the only fields outside the policy that touch MPJPO must be the ones whose " +
            "disable is behind a GameMoment test; anything else here is a door being " +
            "offered that the game will not open");

        // And the two the mod already knew about independently: the Corel Prison drop point,
        // and the Highwind cockpit whose way out is entity 6's own [OK] line.
        Equal(false, FieldGatewayTriggerPolicy.AreGatewayTriggersUsable(471),
            "jail1's gateways are disabled by its own Director");
        Equal(false, FieldGatewayTriggerPolicy.AreGatewayTriggersUsable(70),
            "the Highwind cockpit's gateway is disabled; the [OK] line is the way out");
        Equal(false, FieldGatewayTriggerPolicy.AreGatewayTriggersUsable(512),
            "the Chocobo Square racing room's only gateway cannot fire");

        // crcin_1 disables and then re-enables, so it must keep its doors.
        Equal(true, FieldGatewayTriggerPolicy.AreGatewayTriggersUsable(511),
            "a field that re-enables its gateways must keep them");
    }

    private static void AFieldThatDisablesItsGatewaysOffersNoneOfThem()
    {
        var memory = new GatewayMemory(512);
        var reader = new FieldGatewayTargetReader(memory);
        Equal(true, reader.TryReadTargets(Position(512), out var targets),
            "the gateway table is still readable; it is the doors that are dead");
        Equal(0, targets.Count,
            "the Chocobo Square racing room must offer no gateway exit at all: auto walk " +
            "drove at this one for ninety-nine samples in the recorded session");
        Equal(true, reader.LastDiagnostic.Contains("disables every gateway trigger", StringComparison.Ordinal),
            $"and must say why: {reader.LastDiagnostic}");
    }

    private static void AnOrdinaryFieldStillOffersItsGateways()
    {
        var memory = new GatewayMemory(450);
        var reader = new FieldGatewayTargetReader(memory);
        Equal(true, reader.TryReadTargets(Position(450), out var targets),
            "an ordinary field reads normally");
        Equal(1, targets.Count, "and keeps the door in its table");
        Equal(true, targets[0].TriggerLine is { StartX: 100, StartY: 200 },
            "with the geometry the table gives");
    }

    /// <summary>
    /// Any branch inside Init that jumps over the gateway disable. Empty when the disable
    /// cannot be skipped.
    ///
    /// <para>Init is the run of opcodes up to the script's first RET. A conditional's
    /// false target is <c>byteIndex + operandIndex + operand</c>, the same arithmetic the
    /// navigation catalog uses, with operand index 5 for the byte comparisons and 7 for the
    /// word ones; an unconditional JMPF is measured from its own operand.</para>
    /// </summary>
    private static string DescribeBypasses(FieldScriptDefinition director)
    {
        var init = new List<FieldScriptOpcodeDefinition>();
        foreach (var opcode in director.Opcodes.OrderBy(opcode => opcode.ByteIndex))
        {
            if (opcode.Opcode == 0x00)
            {
                break;
            }

            init.Add(opcode);
        }

        var disable = init.FirstOrDefault(IsDisableAllGateways);
        if (disable.Bytes is null)
        {
            return "no disabling MPJPO in Init at all";
        }

        var bypasses = new List<string>();
        foreach (var opcode in init)
        {
            if (opcode.ByteIndex >= disable.ByteIndex)
            {
                break;
            }

            if (TryResolveForwardTarget(opcode, out var target) && target > disable.ByteIndex)
            {
                bypasses.Add($"0x{opcode.Opcode:X2}@{opcode.ByteIndex}->{target}");
            }
        }

        return string.Join(", ", bypasses);
    }

    private static bool TryResolveForwardTarget(FieldScriptOpcodeDefinition opcode, out int target)
    {
        target = 0;
        var bytes = opcode.Bytes;
        switch (opcode.Opcode)
        {
            case 0x10 when bytes.Count >= 2: // JMPF
                target = opcode.ByteIndex + 1 + bytes[1];
                return true;
            case 0x11 when bytes.Count >= 3: // JMPFL
                target = opcode.ByteIndex + 1 + (bytes[1] | (bytes[2] << 8));
                return true;
            case 0x14 when bytes.Count >= 6: // IFUB
                target = opcode.ByteIndex + 5 + bytes[5];
                return true;
            case 0x16 when bytes.Count >= 8: // IFSW
            case 0x18 when bytes.Count >= 8: // IFUW
                target = opcode.ByteIndex + 7 + bytes[7];
                return true;
            case 0x15 when bytes.Count >= 7: // IFUBL
                target = opcode.ByteIndex + 5 + (bytes[5] | (bytes[6] << 8));
                return true;
            case 0x17 when bytes.Count >= 9: // IFSWL
            case 0x19 when bytes.Count >= 9: // IFUWL
                target = opcode.ByteIndex + 7 + (bytes[7] | (bytes[8] << 8));
                return true;
            default:
                return false;
        }
    }

    private static FieldPositionSnapshot Position(int fieldId) =>
        new(FieldPositionReader.FieldModule, fieldId, 0, 0, 0, 0, 0, 0);

    private static bool IsDisableAllGateways(FieldScriptOpcodeDefinition opcode) =>
        opcode.Opcode == GatewayTriggerToggleOpcode &&
        opcode.Bytes.Count >= 2 &&
        opcode.Bytes[1] != 0;

    private static bool IsEnableAllGateways(FieldScriptOpcodeDefinition opcode) =>
        opcode.Opcode == GatewayTriggerToggleOpcode &&
        opcode.Bytes.Count >= 2 &&
        opcode.Bytes[1] == 0;

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Field gateway trigger policy: {message}. Expected {expected}, actual {actual}.");
        }
    }

    /// <summary>
    /// One gateway in the table, so the only thing deciding whether it is offered is the
    /// policy.
    /// </summary>
    private sealed class GatewayMemory(int fieldId) : ILegacyAddressSpace
    {
        private const uint TriggerPointer = 0x00200000;

        public bool TryRead(uint address, Span<byte> destination)
        {
            destination.Clear();
            if (address == (uint)FieldPositionReader.AddressCurrentModule && destination.Length >= 1)
            {
                destination[0] = FieldPositionReader.FieldModule;
                return true;
            }

            if (address == (uint)FieldPositionReader.AddressFieldId && destination.Length >= 2)
            {
                Write(destination, 0, (short)fieldId);
                return true;
            }

            if (address == (uint)FieldNavigationControlReader.AddressFieldTriggersPtr &&
                destination.Length >= 4)
            {
                destination[0] = (byte)(TriggerPointer & 0xFF);
                destination[1] = (byte)((TriggerPointer >> 8) & 0xFF);
                destination[2] = (byte)((TriggerPointer >> 16) & 0xFF);
                destination[3] = (byte)((TriggerPointer >> 24) & 0xFF);
                return true;
            }

            if (address != TriggerPointer + FieldGatewayTargetReader.GatewaysOffset)
            {
                return false;
            }

            // One usable door: exit line (100,200,0) to (300,400,0) into field 1. Every
            // other slot is the unused marker the reader already knows to skip.
            Write(destination, 0x00, 100);
            Write(destination, 0x02, 200);
            Write(destination, 0x06, 300);
            Write(destination, 0x08, 400);
            Write(destination, FieldGatewayTargetReader.DestinationFieldOffset, 1);
            for (var index = 1; index < FieldGatewayTargetReader.GatewayCount; index++)
            {
                Write(
                    destination,
                    (index * FieldGatewayTargetReader.GatewayStride) +
                        FieldGatewayTargetReader.DestinationFieldOffset,
                    short.MaxValue);
            }

            return true;
        }

        private static void Write(Span<byte> destination, int offset, short value)
        {
            if (offset + 2 > destination.Length) return;
            destination[offset] = (byte)(value & 0xFF);
            destination[offset + 1] = (byte)((value >> 8) & 0xFF);
        }
    }
}
