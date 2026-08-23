// Read-only evidence dump for every Fort Condor modal/overlay state and the
// menu-selection globals consumed by those states.
//
// The script deliberately discovers functions from direct references to the
// state words before adding the small set of previously anchored dispatchers.
// This prevents a hand-maintained function list from silently omitting a
// writer when module 9 grows another overlay.
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
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

public class DumpCondorModalStates extends GhidraScript {
    private static final long MODAL_STATE = 0x00C625E0L;
    private static final long INTERACTION_MODE = 0x00C74C50L;
    private static final long BATTLE_PHASE = 0x00C625D4L;

    private static final Map<Long, String> GLOBALS = new LinkedHashMap<>();
    private static final Map<Long, String> ANCHORED_FUNCTIONS = new LinkedHashMap<>();

    static {
        GLOBALS.put(MODAL_STATE, "modal_state_u32");
        GLOBALS.put(INTERACTION_MODE, "interaction_mode_u32");
        GLOBALS.put(BATTLE_PHASE, "battle_phase_u32");
        GLOBALS.put(0x00C72DECL, "report_state_i16");
        GLOBALS.put(0x00C72E3CL, "report_unit_slot_i16");
        GLOBALS.put(0x00C60AC4L, "report_message_cell_i16");
        GLOBALS.put(0x00C72DFCL, "report_panel_y_i16");
        GLOBALS.put(0x00C60AE4L, "setting_menu_open_request_i16");
        GLOBALS.put(0x00C6097CL, "selected_unit_slot_i16");
        GLOBALS.put(0x00C60980L, "crowded_unit_pointer_list");
        GLOBALS.put(0x00C61BF4L, "crowded_unit_count_i16");
        GLOBALS.put(0x00C74C68L, "crowded_unit_row_i16");
        GLOBALS.put(0x00CBC930L, "ally_unit_menu_row_i16");
        GLOBALS.put(0x00C752D4L, "ally_unit_menu_row_count_u8");
        GLOBALS.put(0x00C74CA8L, "ally_unit_command_0");
        GLOBALS.put(0x00C74CB0L, "ally_unit_command_1");
        GLOBALS.put(0x00C74CB8L, "ally_unit_command_2");
        GLOBALS.put(0x00CBCCA0L, "setting_menu_row_i16");
        GLOBALS.put(0x00C75254L, "setting_menu_rotation_i16");
        GLOBALS.put(0x00C75264L, "setting_menu_count_i16");
        GLOBALS.put(0x00C75278L, "setting_menu_type_ids");
        GLOBALS.put(0x00CBC7D8L, "start_game_yes_no_selection_i16");
        GLOBALS.put(0x00C625D0L, "direction_selection_i16");
        GLOBALS.put(0x00C75284L, "saved_unit_direction_i16");
        GLOBALS.put(0x00CBC808L, "saved_unit_command_u8");
        GLOBALS.put(0x00C752B4L, "game_speed_i16");
        GLOBALS.put(0x00C75268L, "destination_cursor_x_i16");
        GLOBALS.put(0x00C7526AL, "destination_cursor_y_i16");
        GLOBALS.put(0x00CBC80CL, "module_return_state_u32");
        GLOBALS.put(0x00901B70L, "rendered_message_id_u32");

        ANCHORED_FUNCTIONS.put(0x005F7979L, "battle_state_initialize");
        ANCHORED_FUNCTIONS.put(0x005F7D9BL, "battle_tick_and_result_dispatch");
        ANCHORED_FUNCTIONS.put(0x005F7F9DL, "draw_ally_unit_command_rows");
        ANCHORED_FUNCTIONS.put(0x005F824AL, "ui_render_dispatch");
        ANCHORED_FUNCTIONS.put(0x005F88F3L, "draw_report_panel");
        ANCHORED_FUNCTIONS.put(0x005FB754L, "active_play_ui_update");
        ANCHORED_FUNCTIONS.put(0x005FBCDFL, "modal_state_14_help_update");
        ANCHORED_FUNCTIONS.put(0x005FC2CAL, "return_to_cursor_mode");
        ANCHORED_FUNCTIONS.put(0x005FC96FL, "modal_update_dispatch");
        ANCHORED_FUNCTIONS.put(0x005FCE95L, "modal_state_2_update");
        ANCHORED_FUNCTIONS.put(0x005FD832L, "open_selected_unit_menu");
        ANCHORED_FUNCTIONS.put(0x005FD8C7L, "close_selected_unit_menu");
        ANCHORED_FUNCTIONS.put(0x005FD3A0L, "run_crowded_unit_selector");
        ANCHORED_FUNCTIONS.put(0x005FD958L, "input_and_ui_state_update");
        ANCHORED_FUNCTIONS.put(0x005FE800L, "build_ally_unit_command_rows");
        ANCHORED_FUNCTIONS.put(0x006027C2L, "open_report_panel");
        ANCHORED_FUNCTIONS.put(0x00603230L, "unit_command_dispatch");
        ANCHORED_FUNCTIONS.put(0x0060378BL, "open_setting_menu");
        ANCHORED_FUNCTIONS.put(0x00604208L, "modal_state_7_update");
        ANCHORED_FUNCTIONS.put(0x006046A7L, "modal_state_8_update");
        ANCHORED_FUNCTIONS.put(0x006047ACL, "direction_selection_update");
        ANCHORED_FUNCTIONS.put(0x0060484BL, "modal_state_16_update");
    }

    private DecompInterface decompiler;

    @Override
    public void run() throws Exception {
        println("CONDOR_MODAL_STATE_EVIDENCE program=" + currentProgram.getName());
        println("language=" + currentProgram.getLanguageID());

        decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            Set<Function> discovered = new LinkedHashSet<>();
            println("=== GLOBAL_XREFS ===");
            for (Map.Entry<Long, String> entry : GLOBALS.entrySet()) {
                printGlobalReferences(entry.getKey(), entry.getValue(), discovered);
            }

            for (long raw : ANCHORED_FUNCTIONS.keySet()) {
                Function function = functionAtOrContaining(raw);
                if (function != null) {
                    discovered.add(function);
                }
            }

            // One call layer around every direct state user supplies the draw or
            // input helper that explains what a numeric state means.
            Set<Function> expanded = new LinkedHashSet<>(discovered);
            for (Function function : new ArrayList<>(discovered)) {
                expanded.addAll(function.getCallingFunctions(monitor));
                expanded.addAll(function.getCalledFunctions(monitor));
            }

            List<Function> functions = new ArrayList<>(expanded);
            functions.sort(Comparator.comparing(f -> f.getEntryPoint().getOffset()));
            println("=== DISCOVERED_AND_ADJACENT_FUNCTIONS count=" + functions.size() + " ===");
            for (Function function : functions) {
                printFunction(function);
            }
        }
        finally {
            decompiler.dispose();
        }
    }

    private void printGlobalReferences(long rawAddress, String label, Set<Function> discovered)
            throws Exception {
        List<Reference> references = new ArrayList<>();
        ReferenceIterator iterator = currentProgram.getReferenceManager()
            .getReferencesTo(toAddr(rawAddress));
        while (iterator.hasNext()) {
            references.add(iterator.next());
        }
        references.sort(Comparator.comparing(r -> r.getFromAddress().getOffset()));

        println(String.format("GLOBAL %s 0x%08X references=%d", label, rawAddress,
            references.size()));
        for (Reference reference : references) {
            monitor.checkCancelled();
            Instruction instruction = getInstructionContaining(reference.getFromAddress());
            Function function = getFunctionContaining(reference.getFromAddress());
            if (function != null) {
                discovered.add(function);
            }
            println("  " + reference.getFromAddress() + " " +
                (function == null ? "<no-function>" : function.getName() + "@" + function.getEntryPoint()) +
                " " + reference.getReferenceType() + " " +
                (instruction == null ? "<no-instruction>" : instruction.toString()));
        }
    }

    private void printFunction(Function function) throws Exception {
        monitor.checkCancelled();
        String anchored = ANCHORED_FUNCTIONS.get(function.getEntryPoint().getOffset());
        println("FUNCTION " + (anchored == null ? "discovered" : anchored) + " " +
            function.getName() + "@" + function.getEntryPoint());
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
        callers.sort(Comparator.comparing(r -> r.getFromAddress().getOffset()));
        for (Reference reference : callers) {
            Function caller = getFunctionContaining(reference.getFromAddress());
            println("    " + reference.getFromAddress() + " " +
                (caller == null ? "<no-function>" : caller.getName() + "@" + caller.getEntryPoint()));
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

    private Function functionAtOrContaining(long raw) {
        Function function = getFunctionAt(toAddr(raw));
        return function == null ? getFunctionContaining(toAddr(raw)) : function;
    }
}
