// Read-only evidence dump for Fort Condor ordered-unit movement.
//
// This traces the Action destination confirmation into the live-unit state
// machine, including successful arrival and interruption/failure paths. It
// makes no changes to the analyzed program.
//
// @category FF7.BlindSoldier

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

public class DumpCondorOrderedMovementEvidence extends GhidraScript {
    private static final Map<Long, String> FUNCTIONS = new LinkedHashMap<>();
    private static final Map<Long, String> GLOBALS = new LinkedHashMap<>();

    static {
        FUNCTIONS.put(0x005FD958L, "condor_input_and_ui_update");
        FUNCTIONS.put(0x00603230L, "open_or_update_ally_unit_commands");
        FUNCTIONS.put(0x006033CDL, "commit_selected_unit_command");
        FUNCTIONS.put(0x00603441L, "reset_selected_unit_motion_state");
        FUNCTIONS.put(0x006034E2L, "apply_selected_unit_special_command");
        FUNCTIONS.put(0x005FFAB0L, "update_all_live_units");
        FUNCTIONS.put(0x005FFF45L, "update_one_live_unit_state_machine");
        FUNCTIONS.put(0x006001B2L, "advance_one_live_unit");
        FUNCTIONS.put(0x00600216L, "dispatch_directed_unit_advance");
        FUNCTIONS.put(0x00600247L, "advance_directed_unit_and_report_arrival");
        FUNCTIONS.put(0x0060048CL, "stop_or_reset_unit_motion");
        FUNCTIONS.put(0x00600577L, "handle_unreachable_or_missing_route");
        FUNCTIONS.put(0x0060080BL, "resolve_unit_combat_or_motion_target");
        FUNCTIONS.put(0x00600D9BL, "leave_unit_combat_or_motion_target");
        FUNCTIONS.put(0x006027C2L, "queue_condor_report");
        FUNCTIONS.put(0x00606AFBL, "set_unit_route_or_velocity");
        FUNCTIONS.put(0x00606B41L, "reset_unit_route_or_velocity");

        GLOBALS.put(0x00C6097CL, "selected_live_unit_index_s16");
        GLOBALS.put(0x00CBCCD8L, "unit0_record_base");
        GLOBALS.put(0x00CBCCDAL, "unit0_primary_action_state_u8");
        GLOBALS.put(0x00CBCCDCL, "unit0_secondary_action_state_u8");
        GLOBALS.put(0x00C75268L, "command_destination_x_s16");
        GLOBALS.put(0x00C7526AL, "command_destination_y_s16");
        GLOBALS.put(0x00C72DECL, "report_state");
        GLOBALS.put(0x00C72E3CL, "report_unit_index");
    }

    private DecompInterface decompiler;

    @Override
    public void run() throws Exception {
        println("CONDOR_ORDERED_MOVEMENT_EVIDENCE program=" + currentProgram.getName());
        println("language=" + currentProgram.getLanguageID());
        println("imageBase=" + currentProgram.getImageBase());

        decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            println("=== GLOBAL_XREFS ===");
            for (Map.Entry<Long, String> entry : GLOBALS.entrySet()) {
                printGlobalReferences(entry.getKey(), entry.getValue());
            }

            println("=== FUNCTIONS ===");
            for (Map.Entry<Long, String> entry : FUNCTIONS.entrySet()) {
                printFunction(entry.getKey(), entry.getValue());
            }
        }
        finally {
            decompiler.dispose();
        }
    }

    private void printGlobalReferences(long rawAddress, String label) throws Exception {
        Address address = toAddr(rawAddress);
        List<Reference> references = new ArrayList<>();
        ReferenceIterator iterator = currentProgram.getReferenceManager().getReferencesTo(address);
        while (iterator.hasNext()) {
            references.add(iterator.next());
        }
        references.sort(Comparator.comparing(reference -> reference.getFromAddress().getOffset()));

        println(String.format("GLOBAL %s 0x%08X references=%d", label, rawAddress,
            references.size()));
        for (Reference reference : references) {
            monitor.checkCancelled();
            Instruction instruction = getInstructionContaining(reference.getFromAddress());
            Function function = getFunctionContaining(reference.getFromAddress());
            println("  " + reference.getFromAddress() + " " +
                (function == null ? "<no-function>" : function.getName()) + " " +
                reference.getReferenceType() + " " +
                (instruction == null ? "<no-instruction>" : instruction.toString()));
        }
    }

    private void printFunction(long rawAddress, String label) throws Exception {
        Function function = getFunctionAt(toAddr(rawAddress));
        if (function == null) {
            function = getFunctionContaining(toAddr(rawAddress));
        }

        println(String.format("FUNCTION %s 0x%08X", label, rawAddress));
        if (function == null) {
            println("  missing");
            return;
        }

        println("  ghidra=" + function.getName() + "@" + function.getEntryPoint());
        println("  callers:");
        List<Reference> callers = new ArrayList<>();
        ReferenceIterator iterator = currentProgram.getReferenceManager()
            .getReferencesTo(function.getEntryPoint());
        while (iterator.hasNext()) {
            Reference reference = iterator.next();
            if (reference.getReferenceType().isCall()) {
                callers.add(reference);
            }
        }
        callers.sort(Comparator.comparing(reference -> reference.getFromAddress().getOffset()));
        for (Reference reference : callers) {
            Function caller = getFunctionContaining(reference.getFromAddress());
            println("    " + reference.getFromAddress() + " " +
                (caller == null ? "<no-function>" : caller.getName()));
        }

        println("  instructions:");
        for (Instruction instruction : currentProgram.getListing()
            .getInstructions(function.getBody(), true)) {
            monitor.checkCancelled();
            println("    " + instruction.getAddress() + " " + instruction);
        }

        println("  decompile:");
        DecompileResults result = decompiler.decompileFunction(function, 90, monitor);
        if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
            println(result.getDecompiledFunction().getC());
        }
        else {
            println("    failed=" + result.getErrorMessage());
        }
    }
}
