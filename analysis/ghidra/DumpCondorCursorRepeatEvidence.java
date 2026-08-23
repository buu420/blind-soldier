// Read-only evidence dump for Fort Condor cursor repeat and movement state.
//
// This intentionally prints both decompiler output and exact instructions: the
// repeat threshold and increment mutations are small integer comparisons where
// a decompiler's reconstructed control flow alone can hide an off-by-one.
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

public class DumpCondorCursorRepeatEvidence extends GhidraScript {
    private static final Map<Long, String> FUNCTIONS = new LinkedHashMap<>();
    private static final Map<Long, String> GLOBALS = new LinkedHashMap<>();

    static {
        FUNCTIONS.put(0x005FADD4L, "sample_condor_buttons");
        FUNCTIONS.put(0x005FB0D2L, "reset_cursor_motion_accumulators");
        FUNCTIONS.put(0x005FB13BL, "update_cursor_motion_accumulators");
        FUNCTIONS.put(0x005FB20AL, "start_cursor_motion_accumulators");
        FUNCTIONS.put(0x005FD958L, "condor_input_update");
        FUNCTIONS.put(0x005FE718L, "cursor_input_route");
        FUNCTIONS.put(0x005FE771L, "direction_repeat_update");
        FUNCTIONS.put(0x005FE8CFL, "cursor_move_dispatch");
        FUNCTIONS.put(0x005FE91BL, "cursor_move");

        GLOBALS.put(0x00C72E80L, "condor_input_mask_current");
        GLOBALS.put(0x00C74C48L, "condor_input_auxiliary_mask");
        GLOBALS.put(0x00C74C4CL, "condor_input_mask_previous");
        GLOBALS.put(0x00C74C54L, "condor_input_edges");
        GLOBALS.put(0x00CBC7BCL, "direction_repeat_delay_or_step");
        GLOBALS.put(0x00CBF2A8L, "cursor_motion_accumulator_enabled");
        GLOBALS.put(0x00CBF2BCL, "cursor_horizontal_repeat_increment");
        GLOBALS.put(0x00CBF2C0L, "cursor_vertical_repeat_increment");
        GLOBALS.put(0x00CBCCC0L, "world_cursor_x");
        GLOBALS.put(0x00CBCCC2L, "world_cursor_y");
        GLOBALS.put(0x00C60B00L, "camera_origin_x");
        GLOBALS.put(0x00C60B04L, "camera_origin_y");
        GLOBALS.put(0x00C74C38L, "camera_scroll_accumulator_x");
        GLOBALS.put(0x00C74C3CL, "camera_scroll_accumulator_y");
    }

    private DecompInterface decompiler;

    @Override
    public void run() throws Exception {
        println("CONDOR_CURSOR_REPEAT_EVIDENCE program=" + currentProgram.getName());
        println("language=" + currentProgram.getLanguageID());

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

        println(String.format("GLOBAL %s 0x%08X references=%d", label, rawAddress, references.size()));
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
        for (Instruction instruction : currentProgram.getListing().getInstructions(function.getBody(), true)) {
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
