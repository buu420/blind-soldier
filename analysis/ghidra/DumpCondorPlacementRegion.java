// Focused, read-only evidence dump for Fort Condor placement legality,
// collision geometry, vertical boundaries, and the lifetime of the native
// placement-preview flag in the original x86 FFVII PC executable.
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
import java.util.Set;

public class DumpCondorPlacementRegion extends GhidraScript {
    private static final Map<Long, String> FUNCTIONS = new LinkedHashMap<>();
    private static final Map<Long, String> GLOBALS = new LinkedHashMap<>();

    static {
        // Collision data loading and initialization.
        FUNCTIONS.put(0x005F2F46L, "load_condor_archive_entry");
        FUNCTIONS.put(0x005F3160L, "load_condor_collision_records");
        FUNCTIONS.put(0x005F3378L, "initialize_condor_collision_records");
        FUNCTIONS.put(0x005F434BL, "collision_record_setup_or_render");
        FUNCTIONS.put(0x005F4273L, "load_condor_battle_data_files");
        FUNCTIONS.put(0x005F4A47L, "condor_cursor_ui_callback_a");
        FUNCTIONS.put(0x005F4DF4L, "condor_cursor_ui_callback_b");
        FUNCTIONS.put(0x005F4F11L, "condor_cursor_ui_callback_c");
        FUNCTIONS.put(0x005F5042L, "condor_cursor_ui_callback_d");

        // Per-frame UI, placement predicate, and moving front line.
        FUNCTIONS.put(0x005F7979L, "initialize_condor_battle_state");
        FUNCTIONS.put(0x005F7E10L, "condor_module_tick");
        FUNCTIONS.put(0x005F824AL, "render_condor_cursor_feedback");
        FUNCTIONS.put(0x005FD958L, "per_frame_input_and_ui_update");
        FUNCTIONS.put(0x005FE63CL, "validate_world_cursor_placement");
        FUNCTIONS.put(0x005FE718L, "validate_destination_cursor");
        FUNCTIONS.put(0x005FF38AL, "update_allied_front_line");
        FUNCTIONS.put(0x005FC52BL, "build_2d_vector");
        FUNCTIONS.put(0x005FC684L, "fixed_point_vector_angle");

        // Placement footprint, live-unit overlap, and terrain lookup.
        FUNCTIONS.put(0x00602F7DL, "find_overlapping_live_unit");
        FUNCTIONS.put(0x00606F20L, "test_world_cursor_placement");
        FUNCTIONS.put(0x00607032L, "test_destination_cursor_placement");
        FUNCTIONS.put(0x00607123L, "initialize_live_unit_at_position");
        FUNCTIONS.put(0x0060A450L, "angle_between_collision_points");
        FUNCTIONS.put(0x0060A4C6L, "angle_sum_inside_test");
        FUNCTIONS.put(0x0060A550L, "point_in_collision_polygon");
        FUNCTIONS.put(0x0060A682L, "lookup_collision_polygon");
        FUNCTIONS.put(0x0060A77EL, "collision_neighbor_or_projection");
        FUNCTIONS.put(0x0060A844L, "collision_motion_resolver");
        FUNCTIONS.put(0x0060A9AAL, "collision_record_consumer_a9aa");
        FUNCTIONS.put(0x0060A8F1L, "collision_triangle_edge_test");
        FUNCTIONS.put(0x00606AFBL, "copy_collision_record_result");
        FUNCTIONS.put(0x00606B41L, "collision_record_consumer_606b41");

        GLOBALS.put(0x00CBCC9CL, "cursor_placement_valid_u16");
        GLOBALS.put(0x00CBCC98L, "overlapping_live_unit_count_s16");
        GLOBALS.put(0x00CBCCC0L, "world_cursor_x_s16");
        GLOBALS.put(0x00CBCCC2L, "world_cursor_y_s16");
        GLOBALS.put(0x00C625D4L, "condor_phase_u32");
        GLOBALS.put(0x00C625E0L, "modal_state_u32");
        GLOBALS.put(0x00C625E8L, "collision_record_array");
        GLOBALS.put(0x00C60AA4L, "collision_record_count_u32");
        GLOBALS.put(0x00C60AD0L, "active_allied_unit_count_u32");
        GLOBALS.put(0x00C60AE8L, "placement_front_line_y_u32");
        GLOBALS.put(0x00C6097CL, "selected_unit_index_s16");
        GLOBALS.put(0x00C72DECL, "report_or_scripted_state_s16");
        GLOBALS.put(0x00C752A8L, "cursor_or_map_z_s16");
        GLOBALS.put(0x00CBCCD8L, "live_unit_array");
    }

    private DecompInterface decompiler;

    @Override
    public void run() throws Exception {
        println("CONDOR_PLACEMENT_REGION_EVIDENCE program=" + currentProgram.getName());
        println("language=" + currentProgram.getLanguageID());
        decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            println("=== GLOBAL_XREFS ===");
            for (Map.Entry<Long, String> entry : GLOBALS.entrySet()) {
                printGlobalReferences(entry.getKey(), entry.getValue());
            }

            println("=== FOCUSED_FUNCTIONS ===");
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
        printFunctionSet("callers", function.getCallingFunctions(monitor));
        printFunctionSet("callees", function.getCalledFunctions(monitor));
        DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
        if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
            println(result.getDecompiledFunction().getC());
        }
        else {
            println("  decompileFailed=" + result.getErrorMessage());
        }
    }

    private void printFunctionSet(String label, Set<Function> functions) {
        List<Function> ordered = new ArrayList<>(functions);
        ordered.sort(Comparator.comparing(function -> function.getEntryPoint().getOffset()));
        StringBuilder text = new StringBuilder("  ").append(label).append("=");
        for (int i = 0; i < ordered.size(); i++) {
            if (i != 0) {
                text.append(',');
            }
            Function function = ordered.get(i);
            text.append(function.getName()).append('@').append(function.getEntryPoint());
        }
        println(text.toString());
    }
}
