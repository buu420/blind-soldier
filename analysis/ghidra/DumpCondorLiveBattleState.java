// Focused, read-only static evidence dump for the live Fort Condor battle
// state in the original x86 FFVII PC executable.  This script deliberately
// starts from code consumers/writers rather than from changing-memory scans.
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

public class DumpCondorLiveBattleState extends GhidraScript {
    private static final Map<Long, String> FUNCTIONS = new LinkedHashMap<>();
    private static final Map<Long, String> GLOBALS = new LinkedHashMap<>();

    static {
        // Input, cursor modes, menu creation and placement.
        FUNCTIONS.put(0x005FD958L, "input_and_ui_state_update");
        FUNCTIONS.put(0x005F7756L, "initialize_condor_session_state");
        FUNCTIONS.put(0x005F7818L, "settle_condor_session_rewards");
        FUNCTIONS.put(0x005F7893L, "condor_top_level_update");
        FUNCTIONS.put(0x005F7979L, "initialize_condor_battle_state");
        FUNCTIONS.put(0x005F88F3L, "ui_animation_or_state_update");
        FUNCTIONS.put(0x005F7D33L, "finish_condor_victory_state");
        FUNCTIONS.put(0x005F7D5BL, "finish_condor_defeat_state");
        FUNCTIONS.put(0x005F7D9BL, "consume_condor_message_id");
        FUNCTIONS.put(0x005F7E10L, "condor_module_tick");
        FUNCTIONS.put(0x005F824AL, "condor_result_or_overlay_update");
        FUNCTIONS.put(0x005F86E4L, "render_condor_message_by_id");
        FUNCTIONS.put(0x005FD832L, "open_ally_unit_menu");
        FUNCTIONS.put(0x005FD8C7L, "close_ally_unit_menu");
        FUNCTIONS.put(0x005FCE53L, "condor_start_prompt_input");
        FUNCTIONS.put(0x005FE63CL, "validate_world_cursor_placement");
        FUNCTIONS.put(0x005FE718L, "validate_destination_cursor");
        FUNCTIONS.put(0x005FE8CFL, "cursor_move_dispatch");
        FUNCTIONS.put(0x005FE91BL, "move_world_or_destination_cursor");
        FUNCTIONS.put(0x006029FDL, "unit_hit_test");
        FUNCTIONS.put(0x00603230L, "ally_unit_menu_command");
        FUNCTIONS.put(0x0060378BL, "build_and_open_setting_menu");
        FUNCTIONS.put(0x00603711L, "setting_menu_open_animation_callback");
        FUNCTIONS.put(0x00604009L, "hire_and_place_unit");
        FUNCTIONS.put(0x00604208L, "setting_menu_update");
        FUNCTIONS.put(0x006046A7L, "setting_menu_close_animation");

        // Live-unit allocation, initialization, update, combat and display.
        FUNCTIONS.put(0x005FD3A0L, "selected_unit_detail_builder");
        FUNCTIONS.put(0x005FBD2FL, "live_unit_state_update_or_cleanup");
        FUNCTIONS.put(0x005FF33EL, "live_unit_scan_ff33e");
        FUNCTIONS.put(0x005FF38AL, "live_unit_combat_or_collision_ff38a");
        FUNCTIONS.put(0x005FF740L, "repair_unit_hp");
        FUNCTIONS.put(0x005FFCE9L, "live_unit_distance_or_target_update");
        FUNCTIONS.put(0x005FFF45L, "live_unit_reaches_destination_or_fort");
        FUNCTIONS.put(0x00600247L, "live_unit_update_600247");
        FUNCTIONS.put(0x006008F0L, "live_unit_update_6008f0");
        FUNCTIONS.put(0x00601145L, "live_unit_update_601145");
        FUNCTIONS.put(0x006012D5L, "live_unit_update_6012d5");
        FUNCTIONS.put(0x0060142FL, "live_unit_update_60142f");
        FUNCTIONS.put(0x00601545L, "live_unit_update_601545");
        FUNCTIONS.put(0x006016EAL, "live_unit_update_6016ea");
        FUNCTIONS.put(0x00601890L, "live_unit_update_601890");
        FUNCTIONS.put(0x006019E7L, "render_live_units");
        FUNCTIONS.put(0x00601960L, "calculate_attack_damage");
        FUNCTIONS.put(0x006027C2L, "queue_condor_message");
        FUNCTIONS.put(0x006027B3L, "condor_message_state_accessor");
        FUNCTIONS.put(0x0060286BL, "condor_message_overlay_update");
        FUNCTIONS.put(0x00602F7DL, "find_overlapping_live_unit");
        FUNCTIONS.put(0x006034E2L, "selected_unit_special_command");
        FUNCTIONS.put(0x00603FB4L, "close_setting_menu_or_accept_hire");
        FUNCTIONS.put(0x00604906L, "remove_allied_unit");
        FUNCTIONS.put(0x00604930L, "export_allied_unit_positions");
        FUNCTIONS.put(0x006050A1L, "live_unit_progress_update");
        FUNCTIONS.put(0x00605C51L, "live_unit_state_dispatch");
        FUNCTIONS.put(0x00605E43L, "initialize_live_unit_from_data");
        FUNCTIONS.put(0x0060644EL, "live_unit_ai_or_combat_update");
        FUNCTIONS.put(0x00606A50L, "activate_live_unit");
        FUNCTIONS.put(0x00606AB6L, "initialize_unit_render_state");
        FUNCTIONS.put(0x00606E6BL, "refresh_unit_animation_state");
        FUNCTIONS.put(0x0060692DL, "live_unit_position_integrator");
        FUNCTIONS.put(0x00607517L, "find_free_live_unit_slot");
        FUNCTIONS.put(0x00607570L, "remove_live_unit_and_update_counts");
        FUNCTIONS.put(0x00607727L, "enemy_wave_or_spawn_update");
        FUNCTIONS.put(0x006091FCL, "build_funds_counters");
        FUNCTIONS.put(0x006092C2L, "build_unit_name_text");
        FUNCTIONS.put(0x006098CDL, "build_selected_unit_hp_text");
        FUNCTIONS.put(0x006099F8L, "build_condor_message_text");
        FUNCTIONS.put(0x00609F03L, "select_condor_message_texture_region");

        // Coordinate/collision and data.bin consumers.
        FUNCTIONS.put(0x005F33B4L, "load_data_bin");
        FUNCTIONS.put(0x005F2F46L, "load_condor_archive_entry");
        FUNCTIONS.put(0x006036BFL, "test_unit_type_availability");
        FUNCTIONS.put(0x00606AA4L, "coordinate_helper_606aa4");
        FUNCTIONS.put(0x00606B41L, "data_or_map_consumer_606b41");
        FUNCTIONS.put(0x00606F20L, "test_world_cursor_placement");
        FUNCTIONS.put(0x00607032L, "test_destination_cursor_placement");
        FUNCTIONS.put(0x00607123L, "coordinate_helper_607123");
        FUNCTIONS.put(0x0060747CL, "initialize_unit_from_wave_entry");
        FUNCTIONS.put(0x00609420L, "build_unit_stat_panel");
        FUNCTIONS.put(0x00609748L, "build_gil_and_price_text");
        FUNCTIONS.put(0x0060A682L, "lookup_condor_terrain_or_collision");

        // Confirm/menu state.  Some symbols overlap; widths are established by
        // the instructions printed with each reference.
        GLOBALS.put(0x00C74C54L, "input_pressed_edges_u32");
        GLOBALS.put(0x00C74C50L, "interaction_mode_u32");
        GLOBALS.put(0x00C625E0L, "modal_state_u32");
        GLOBALS.put(0x00C6097CL, "selected_unit_index_s16");
        GLOBALS.put(0x00CBCC9CL, "cursor_placement_valid_u16");
        GLOBALS.put(0x00C60AE4L, "setting_menu_open_animation_s16");
        GLOBALS.put(0x00C60AD0L, "active_allied_unit_count_u32");
        GLOBALS.put(0x00C60AE8L, "placement_front_line_y_s16");
        GLOBALS.put(0x00C625D4L, "condor_phase_u32");
        GLOBALS.put(0x00C625E8L, "condor_collision_record_base");
        GLOBALS.put(0x00C60AA4L, "condor_collision_record_count_u32");
        GLOBALS.put(0x00C72DECL, "report_overlay_or_scripted_state_u32");
        GLOBALS.put(0x00C60AC4L, "condor_message_id_s16");
        GLOBALS.put(0x00901B70L, "condor_current_message_id_s32");
        GLOBALS.put(0x00901B78L, "condor_current_unit_name_id_s32");
        GLOBALS.put(0x00C72E7CL, "condor_campaign_or_battle_tier_u16");

        // Setting Menu and funds/progress candidates.
        GLOBALS.put(0x00CBCCA0L, "setting_menu_relative_row_s16");
        GLOBALS.put(0x00C75254L, "setting_menu_rotation_index_s16");
        GLOBALS.put(0x00C75264L, "setting_menu_unit_count_s16");
        GLOBALS.put(0x00C75278L, "setting_menu_unit_id_0_u8");
        GLOBALS.put(0x00CBC7E0L, "current_gil_u32");
        GLOBALS.put(0x00CBC7E4L, "selected_unit_price_u32");
        GLOBALS.put(0x00CBC7A4L, "active_enemy_unit_count_u32");
        GLOBALS.put(0x00CBCC8CL, "enemy_wave_spawn_index_u32");
        GLOBALS.put(0x00CBEDD8L, "enemy_wave_table_index_u16");
        GLOBALS.put(0x00C625A8L, "enemy_spawn_elapsed_frames_u32");
        GLOBALS.put(0x00C72E48L, "next_enemy_spawn_frame_u16");
        GLOBALS.put(0x00CBEDC0L, "fort_destroyed_or_invasion_flag_u16");
        GLOBALS.put(0x00C752B4L, "condor_game_speed_u16");

        // data.bin resident pointer.
        GLOBALS.put(0x00C606F0L, "data_bin_resident_base_pointer");

        // Live unit array, slot zero field anchors.  The code increments a
        // short* by 0x3c, proving a byte stride of 0x78.
        GLOBALS.put(0x00CBCCD8L, "unit0_active_u16");
        GLOBALS.put(0x00CBCCDDL, "unit0_state_u8");
        GLOBALS.put(0x00CBCCDEL, "unit0_type_u16");
        GLOBALS.put(0x00CBCCE8L, "unit0_current_hp_u8");
        GLOBALS.put(0x00CBCCE9L, "unit0_max_hp_u8");
        GLOBALS.put(0x00CBCCEAL, "unit0_attack_u8");
        GLOBALS.put(0x00CBCCEBL, "unit0_range_or_class_u8");
        GLOBALS.put(0x00CBCCF9L, "unit0_name_or_variant_u8");
        GLOBALS.put(0x00CBCD0CL, "unit0_field_34");
        GLOBALS.put(0x00CBCD10L, "unit0_field_38");
        GLOBALS.put(0x00CBCD20L, "unit0_world_x_s16");
        GLOBALS.put(0x00CBCD22L, "unit0_world_y_s16");
        GLOBALS.put(0x00CBCD28L, "unit0_render_or_target_x_s16");
        GLOBALS.put(0x00CBCD2AL, "unit0_render_or_target_y_s16");
        GLOBALS.put(0x00CBCD1BL, "unit0_affinity_or_class_u8");
    }

    private DecompInterface decompiler;

    @Override
    public void run() throws Exception {
        println("CONDOR_LIVE_BATTLE_STATE_EVIDENCE program=" + currentProgram.getName());
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
