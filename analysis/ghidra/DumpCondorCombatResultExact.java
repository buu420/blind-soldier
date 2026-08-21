// Instruction-addressed, read-only evidence for the Fort Condor placement,
// name rendering, progress, and result paths in the original x86 FFVII PC
// executable.
//
// @category FF7.BlindSoldier

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

public class DumpCondorCombatResultExact extends GhidraScript {
    private static final Map<Long, String> INSTRUCTION_FUNCTIONS = new LinkedHashMap<>();
    private static final Map<Long, String> DECOMPILE_ONLY_FUNCTIONS = new LinkedHashMap<>();
    private static final Map<Long, String> GLOBALS = new LinkedHashMap<>();

    static {
        // Q1: phase, complete placement gate, direct hit, overlap, and frontier.
        INSTRUCTION_FUNCTIONS.put(0x005F7756L, "initialize_condor_session");
        INSTRUCTION_FUNCTIONS.put(0x005F7893L, "dispatch_condor_substate");
        INSTRUCTION_FUNCTIONS.put(0x005F7979L, "initialize_condor_battle_state");
        INSTRUCTION_FUNCTIONS.put(0x005FE63CL, "validate_world_cursor_placement");
        INSTRUCTION_FUNCTIONS.put(0x006029FDL, "find_unit_under_cursor");
        INSTRUCTION_FUNCTIONS.put(0x00602F7DL, "scan_live_unit_overlap");
        INSTRUCTION_FUNCTIONS.put(0x00606F20L, "test_world_cursor_terrain_and_overlap");
        INSTRUCTION_FUNCTIONS.put(0x005FF38AL, "update_units_frontier_and_advance_gauge");
        INSTRUCTION_FUNCTIONS.put(0x00607570L, "initialize_units_for_phase");
        INSTRUCTION_FUNCTIONS.put(0x00607727L, "spawn_next_enemy_script_entry");

        // Q2: texture-to-region-to-type-name path.
        INSTRUCTION_FUNCTIONS.put(0x005F2CE4L, "bind_loaded_condor_textures");
        INSTRUCTION_FUNCTIONS.put(0x005F5678L, "initialize_condor_render_resources");
        INSTRUCTION_FUNCTIONS.put(0x005FA132L, "draw_unit_name_and_number");
        INSTRUCTION_FUNCTIONS.put(0x006092C2L, "build_unit_name_display");
        INSTRUCTION_FUNCTIONS.put(0x00607BCDL, "store_condor_sprite_region");
        INSTRUCTION_FUNCTIONS.put(0x005F933FL, "initialize_condor_sprite_regions");
        INSTRUCTION_FUNCTIONS.put(0x005F88F3L, "render_condor_hud_and_unit_panel");
        INSTRUCTION_FUNCTIONS.put(0x005F80F1L, "render_condor_unit_count_digits");
        DECOMPILE_ONLY_FUNCTIONS.put(0x005F342CL, "load_condor_textures");
        DECOMPILE_ONLY_FUNCTIONS.put(0x00607CC5L, "draw_condor_sprite_region");

        // Q3/Q4: end triggers, overlays, result export, and module exit.
        INSTRUCTION_FUNCTIONS.put(0x0060747CL, "initialize_unit_from_spawn_record");
        INSTRUCTION_FUNCTIONS.put(0x00607123L, "initialize_live_unit");
        INSTRUCTION_FUNCTIONS.put(0x006091FCL, "format_live_unit_counts_for_hud");
        INSTRUCTION_FUNCTIONS.put(0x005F824AL, "render_condor_battle_hud");
        INSTRUCTION_FUNCTIONS.put(0x005FBD2FL, "update_allied_unit_state_and_count");
        INSTRUCTION_FUNCTIONS.put(0x006050A1L, "update_enemy_unit_state_and_count");
        INSTRUCTION_FUNCTIONS.put(0x006008F0L, "enemy_unit_damage_or_removal_logic");
        INSTRUCTION_FUNCTIONS.put(0x005FFF45L, "update_enemy_unit_and_invasion_trigger");
        INSTRUCTION_FUNCTIONS.put(0x005F7818L, "export_condor_session_result");
        INSTRUCTION_FUNCTIONS.put(0x006027B3L, "queue_condor_banner_message");
        INSTRUCTION_FUNCTIONS.put(0x005F7CD5L, "reset_condor_overlay_state");
        INSTRUCTION_FUNCTIONS.put(0x005F7D33L, "begin_victory_overlay");
        INSTRUCTION_FUNCTIONS.put(0x005F7D5BL, "begin_invasion_overlay");
        INSTRUCTION_FUNCTIONS.put(0x005F7D9BL, "publish_pending_banner_message");
        INSTRUCTION_FUNCTIONS.put(0x005F7E10L, "condor_battle_tick");
        INSTRUCTION_FUNCTIONS.put(0x005FCA8EL, "handle_victory_overlay_input");
        INSTRUCTION_FUNCTIONS.put(0x005FCAE6L, "handle_invasion_overlay_input");
        INSTRUCTION_FUNCTIONS.put(0x005FC96FL, "dispatch_condor_overlay_handler");
        INSTRUCTION_FUNCTIONS.put(0x005F4971L, "request_condor_game_object_shutdown");
        INSTRUCTION_FUNCTIONS.put(0x005F49C0L, "condor_module_leave");
        INSTRUCTION_FUNCTIONS.put(0x005F4A47L, "condor_main_callback");
        DECOMPILE_ONLY_FUNCTIONS.put(0x005FD958L, "dispatch_condor_modal_input");
        DECOMPILE_ONLY_FUNCTIONS.put(0x005F4DF4L, "condor_callback_4df4");
        DECOMPILE_ONLY_FUNCTIONS.put(0x005F4F11L, "condor_callback_4f11");
        DECOMPILE_ONLY_FUNCTIONS.put(0x005F5042L, "condor_simulation_render_callback");

        GLOBALS.put(0x00C625D4L, "phase_u32");
        GLOBALS.put(0x00C72DECL, "report_state_s16");
        GLOBALS.put(0x00C6097CL, "unit_under_cursor_s16");
        GLOBALS.put(0x00C60AD0L, "active_allies_u32");
        GLOBALS.put(0x00CBC7A4L, "active_enemies_u32");
        GLOBALS.put(0x00CBCC8CL, "spawned_count_and_next_entry_u32");
        GLOBALS.put(0x00CBEDD8L, "encounter_spawn_script_id_s16");
        GLOBALS.put(0x00C625A8L, "spawn_ticks_u32");
        GLOBALS.put(0x00C72E48L, "next_spawn_threshold_u16");
        GLOBALS.put(0x00C60AE8L, "placement_frontier_y_u32");
        GLOBALS.put(0x00CBCCACL, "enemy_advance_gauge_s32_low_s16_rendered");
        GLOBALS.put(0x00CBEDC0L, "battle_end_transition_s16");
        GLOBALS.put(0x00CBC80CL, "condor_control_or_return_state_u32");
        GLOBALS.put(0x00DC0988L, "persistent_condor_result_u8");
        GLOBALS.put(0x00901B70L, "banner_message_id_s32");

        GLOBALS.put(0x00CBF080L, "allied_count_tens_digit_region");
        GLOBALS.put(0x00CBF084L, "allied_count_ones_digit_region");
        GLOBALS.put(0x00CBF0C8L, "enemy_count_tens_digit_region");
        GLOBALS.put(0x00CBF0CCL, "enemy_count_ones_digit_region");
        GLOBALS.put(0x00CBCCF8L, "unit_slot0_spawn_record_byte6_at_plus_20");
        GLOBALS.put(0x00CBCCFCL, "unit_slot0_spawn_record_byte3_at_plus_24");

        GLOBALS.put(0x00C60728L, "emes01_texture_handle");
        GLOBALS.put(0x00C60718L, "eunit01_texture_handle");
        GLOBALS.put(0x00C60550L, "condor_texture_binding_table");
        GLOBALS.put(0x00C60598L, "condor_texture_binding_slot_6");
        GLOBALS.put(0x00C72EA0L, "sprite_region_texture_index_field");
        GLOBALS.put(0x00901B78L, "current_unit_name_type_id_s32");
        GLOBALS.put(0x00CBF110L, "unit_name_type_id_s32");

        // Function-entry xrefs needed to prove the result-overlay dispatch and
        // the module-leave callback chain without relying on guessed labels.
        GLOBALS.put(0x005FCA8EL, "victory_overlay_handler_entry");
        GLOBALS.put(0x005FCAE6L, "invasion_overlay_handler_entry");
        GLOBALS.put(0x005F4971L, "shutdown_request_entry");
        GLOBALS.put(0x005F49C0L, "module_leave_entry");
    }

    private DecompInterface decompiler;

    @Override
    public void run() throws Exception {
        println("CONDOR_COMBAT_RESULT_EXACT program=" + currentProgram.getName());
        println("language=" + currentProgram.getLanguageID());
        decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            println("=== GLOBAL_XREFS ===");
            for (Map.Entry<Long, String> entry : GLOBALS.entrySet()) {
                printGlobalReferences(entry.getKey(), entry.getValue());
            }
            println("=== INSTRUCTION_ADDRESSED_FUNCTIONS ===");
            for (Map.Entry<Long, String> entry : INSTRUCTION_FUNCTIONS.entrySet()) {
                printFunction(entry.getKey(), entry.getValue(), true);
            }
            println("=== DECOMPILE_ONLY_FUNCTIONS ===");
            for (Map.Entry<Long, String> entry : DECOMPILE_ONLY_FUNCTIONS.entrySet()) {
                printFunction(entry.getKey(), entry.getValue(), false);
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

    private void printFunction(long rawAddress, String label, boolean includeInstructions) throws Exception {
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
        if (includeInstructions) {
            println("  INSTRUCTIONS");
            InstructionIterator instructions = currentProgram.getListing().getInstructions(function.getBody(), true);
            while (instructions.hasNext()) {
                monitor.checkCancelled();
                Instruction instruction = instructions.next();
                println("    " + instruction.getAddress() + " " + instruction.toString());
            }
        }
        DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
        if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
            println("  DECOMPILE");
            println(result.getDecompiledFunction().getC());
        }
        else {
            println("  decompileFailed=" + result.getErrorMessage());
        }
    }
}
