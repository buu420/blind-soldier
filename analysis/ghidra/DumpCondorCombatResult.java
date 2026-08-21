// Focused, read-only evidence dump for Fort Condor combat completion,
// progress, placement, phase state, and unit-name rendering in the original
// x86 FFVII PC executable.
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

public class DumpCondorCombatResult extends GhidraScript {
    private static final Map<Long, String> FUNCTIONS = new LinkedHashMap<>();
    private static final Map<Long, String> GLOBALS = new LinkedHashMap<>();

    static {
        // Module entry, callbacks, teardown, and persistent result export.
        FUNCTIONS.put(0x005F47B5L, "condor_module_enter");
        FUNCTIONS.put(0x005F4971L, "request_condor_game_object_shutdown");
        FUNCTIONS.put(0x005F49C0L, "condor_module_leave");
        FUNCTIONS.put(0x005F4A47L, "condor_main_callback");
        FUNCTIONS.put(0x005F4DF4L, "condor_callback_4df4");
        FUNCTIONS.put(0x005F4F11L, "condor_callback_4f11");
        FUNCTIONS.put(0x005F5042L, "condor_simulation_render_callback");
        FUNCTIONS.put(0x005F7756L, "initialize_condor_session");
        FUNCTIONS.put(0x005F7818L, "export_condor_session_result");
        FUNCTIONS.put(0x005F7893L, "dispatch_condor_substate");
        FUNCTIONS.put(0x005F7979L, "initialize_condor_battle_state");
        FUNCTIONS.put(0x005F7CD5L, "begin_condor_result_transition");
        FUNCTIONS.put(0x005F7D33L, "begin_condor_victory_overlay");
        FUNCTIONS.put(0x005F7D5BL, "begin_condor_invasion_overlay");
        FUNCTIONS.put(0x005F7E10L, "condor_battle_tick");
        FUNCTIONS.put(0x005F824AL, "render_condor_state");
        FUNCTIONS.put(0x005F88F3L, "render_condor_overlays");
        FUNCTIONS.put(0x005FA132L, "draw_unit_name_and_number");
        FUNCTIONS.put(0x005FC96FL, "dispatch_condor_modal_input");
        FUNCTIONS.put(0x005FCA8EL, "handle_victory_overlay_input");
        FUNCTIONS.put(0x005FCAE6L, "handle_invasion_overlay_input");
        FUNCTIONS.put(0x005FCB75L, "handle_start_or_quit_prompt_input");
        FUNCTIONS.put(0x005FCD6DL, "handle_start_battle_transition");

        // Placement and progress.
        FUNCTIONS.put(0x005FE63CL, "validate_world_cursor_placement");
        FUNCTIONS.put(0x005FF38AL, "update_live_units_and_deployment_frontier");
        FUNCTIONS.put(0x00602F7DL, "scan_live_unit_overlap");
        FUNCTIONS.put(0x00606F20L, "test_world_cursor_terrain_and_overlap");
        FUNCTIONS.put(0x00607123L, "initialize_live_unit");
        FUNCTIONS.put(0x00607570L, "initialize_or_remove_condor_units");
        FUNCTIONS.put(0x00607727L, "spawn_enemy_wave_entry");
        FUNCTIONS.put(0x00607B91L, "define_condor_sprite_region");
        FUNCTIONS.put(0x00607BCDL, "store_condor_sprite_region");

        // Name/stat panel rendering.
        FUNCTIONS.put(0x005F342CL, "load_condor_textures");
        FUNCTIONS.put(0x005F933FL, "initialize_condor_sprite_regions");
        FUNCTIONS.put(0x006019E7L, "render_live_units");
        FUNCTIONS.put(0x006092C2L, "build_unit_name_display");
        FUNCTIONS.put(0x00609420L, "build_unit_stat_panel");
        FUNCTIONS.put(0x006099D9L, "render_condor_text_buffers");
        FUNCTIONS.put(0x00609AABL, "render_one_condor_text_buffer");
        FUNCTIONS.put(0x00607CC5L, "draw_condor_sprite_region");

        GLOBALS.put(0x00C625D4L, "condor_phase_u32");
        GLOBALS.put(0x00C625E0L, "condor_modal_state_u32");
        GLOBALS.put(0x00CBEDC0L, "battle_end_transition_s16");
        GLOBALS.put(0x00CBC80CL, "condor_control_or_return_state_u32");
        GLOBALS.put(0x00DC0985L, "persistent_wave_id_u8");
        GLOBALS.put(0x00DC0986L, "persistent_allied_count_u8");
        GLOBALS.put(0x00DC0987L, "persistent_spawn_delta_u8");
        GLOBALS.put(0x00DC0988L, "persistent_condor_result_u8");
        GLOBALS.put(0x00DC098BL, "persistent_enemy_count_u8");
        GLOBALS.put(0x00DC08B4L, "persistent_condor_gil_u32");
        GLOBALS.put(0x00C60AD0L, "active_allied_count_u32");
        GLOBALS.put(0x00CBC7A4L, "active_enemy_count_u32");
        GLOBALS.put(0x00CBCC8CL, "next_enemy_wave_entry_u32");
        GLOBALS.put(0x00CBEDD8L, "selected_enemy_wave_id_s16");
        GLOBALS.put(0x00C625A8L, "enemy_spawn_ticks_u32");
        GLOBALS.put(0x00C72E48L, "next_enemy_spawn_threshold_u16");
        GLOBALS.put(0x00C60AE8L, "deployment_frontier_y_u32");
        GLOBALS.put(0x00CBCCACL, "enemy_advance_gauge_s16");
        GLOBALS.put(0x00C72DECL, "report_state_s16");
        GLOBALS.put(0x00CBCC9CL, "placement_preview_u16");
        GLOBALS.put(0x00901B70L, "current_banner_message_id_s32");
        GLOBALS.put(0x00901B74L, "current_stat_panel_type_id_s32");
        GLOBALS.put(0x00901B78L, "current_unit_name_type_id_s32");
        GLOBALS.put(0x00CBF110L, "unit_name_text_buffer_0");
        GLOBALS.put(0x00CBCCD8L, "live_unit_array");
    }

    private DecompInterface decompiler;

    @Override
    public void run() throws Exception {
        println("CONDOR_COMBAT_RESULT_EVIDENCE program=" + currentProgram.getName());
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
        references.sort(Comparator.comparing(r -> r.getFromAddress().getOffset()));
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
        ordered.sort(Comparator.comparing(f -> f.getEntryPoint().getOffset()));
        StringBuilder text = new StringBuilder("  ").append(label).append("=");
        for (int i = 0; i < ordered.size(); i++) {
            if (i != 0) text.append(',');
            Function function = ordered.get(i);
            text.append(function.getName()).append('@').append(function.getEntryPoint());
        }
        println(text.toString());
    }
}
