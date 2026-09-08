// Read-only evidence dump for Fort Condor's mode-3 destination cursor.
//
// @category FF7.BlindSoldier

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

public class DumpCondorDestinationCursorEvidence extends GhidraScript {
    private static final Map<Long, String> GLOBALS = new LinkedHashMap<>();
    private static final Map<Long, String> FUNCTIONS = new LinkedHashMap<>();

    static {
        GLOBALS.put(0x00C75268L, "destination_x_i16");
        GLOBALS.put(0x00C7526AL, "destination_y_i16");
        GLOBALS.put(0x00CBCCC0L, "battlefield_cursor_x_i16");
        GLOBALS.put(0x00CBCCC2L, "battlefield_cursor_y_i16");
        GLOBALS.put(0x00C74C50L, "interaction_mode_u32");
        GLOBALS.put(0x00C625E0L, "modal_state_u32");
        GLOBALS.put(0x00C72DECL, "report_state_i16");
        GLOBALS.put(0x00C72E80L, "condor_held_mask_u32");
        GLOBALS.put(0x00CBC7BCL, "direction_repeat_counter_u32");
        GLOBALS.put(0x00C60B00L, "camera_origin_x_i32");
        GLOBALS.put(0x00C60B04L, "camera_origin_y_i32");
        GLOBALS.put(0x00C74C38L, "scroll_accumulator_x_i32");
        GLOBALS.put(0x00C74C3CL, "scroll_accumulator_y_i32");
        GLOBALS.put(0x00C6097CL, "selected_unit_slot_i16");

        FUNCTIONS.put(0x005FD958L, "condor_input_dispatch");
        FUNCTIONS.put(0x005FE771L, "direction_repeat_dispatch");
        FUNCTIONS.put(0x005FE8CFL, "move_active_cursor");
        FUNCTIONS.put(0x005FE91BL, "move_battlefield_cursor_and_camera");
        FUNCTIONS.put(0x00600247L, "advance_unit_to_destination");
        FUNCTIONS.put(0x00603230L, "dispatch_selected_unit_command");
    }

    private DecompInterface decompiler;

    @Override
    public void run() throws Exception {
        println("CONDOR_DESTINATION_CURSOR_EVIDENCE program=" + currentProgram.getName());
        println("language=" + currentProgram.getLanguageID());

        decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            Set<Function> functions = new LinkedHashSet<>();
            println("=== GLOBAL_XREFS ===");
            for (Map.Entry<Long, String> entry : GLOBALS.entrySet()) {
                printReferences(entry.getKey(), entry.getValue(), functions);
            }

            for (long address : FUNCTIONS.keySet()) {
                Function function = functionAtOrContaining(address);
                if (function != null) {
                    functions.add(function);
                }
            }

            List<Function> ordered = new ArrayList<>(functions);
            ordered.sort(Comparator.comparing(function -> function.getEntryPoint().getOffset()));
            println("=== FUNCTIONS count=" + ordered.size() + " ===");
            for (Function function : ordered) {
                printFunction(function);
            }
        }
        finally {
            decompiler.dispose();
        }
    }

    private void printReferences(long address, String label, Set<Function> functions)
            throws Exception {
        List<Reference> references = new ArrayList<>();
        ReferenceIterator iterator = currentProgram.getReferenceManager().getReferencesTo(toAddr(address));
        while (iterator.hasNext()) {
            references.add(iterator.next());
        }
        references.sort(Comparator.comparing(reference -> reference.getFromAddress().getOffset()));

        println(String.format("GLOBAL %s 0x%08X references=%d", label, address, references.size()));
        for (Reference reference : references) {
            monitor.checkCancelled();
            Instruction instruction = getInstructionContaining(reference.getFromAddress());
            Function function = getFunctionContaining(reference.getFromAddress());
            if (function != null) {
                functions.add(function);
            }
            println("  " + reference.getFromAddress() + " " +
                (function == null ? "<no-function>" : function.getName() + "@" + function.getEntryPoint()) +
                " " + reference.getReferenceType() + " " +
                (instruction == null ? "<no-instruction>" : instruction.toString()));
        }
    }

    private void printFunction(Function function) throws Exception {
        monitor.checkCancelled();
        String label = FUNCTIONS.get(function.getEntryPoint().getOffset());
        println("FUNCTION " + (label == null ? "discovered" : label) + " " +
            function.getName() + "@" + function.getEntryPoint());
        DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
        if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
            println(result.getDecompiledFunction().getC());
        }
        else {
            println("  decompile failed=" + result.getErrorMessage());
        }
    }

    private Function functionAtOrContaining(long address) {
        Function function = getFunctionAt(toAddr(address));
        return function == null ? getFunctionContaining(toAddr(address)) : function;
    }
}
