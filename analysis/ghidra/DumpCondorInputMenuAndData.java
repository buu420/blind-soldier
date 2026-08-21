// Focused, read-only evidence dump for the original FFVII PC Fort Condor
// minigame.  The addresses are reached from FFNx's documented Condor anchors,
// not from a changing-memory signature scan.
//
// Run after normal Ghidra analysis of ff7_en.exe.  The report contains:
//   * the Condor main loop and per-frame input/state path;
//   * every code reference to the input, cursor, selection, and menu globals;
//   * the data.bin/id.bin loader path and references to its resident globals;
//   * decompilation of the small set of functions needed to reproduce the
//     conclusions without relying on Ghidra names from another project.
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

public class DumpCondorInputMenuAndData extends GhidraScript {
    private static final Map<Long, String> FUNCTIONS = new LinkedHashMap<>();
    private static final Map<Long, String> GLOBALS = new LinkedHashMap<>();

    static {
        FUNCTIONS.put(0x005F4A47L, "condor_main_loop");
        FUNCTIONS.put(0x005F5042L, "condor_tick");
        FUNCTIONS.put(0x005FD958L, "input_and_ui_state_update");
        FUNCTIONS.put(0x005FADD4L, "sample_condor_buttons");
        FUNCTIONS.put(0x005FADB6L, "direction_button_test");
        FUNCTIONS.put(0x005FAFBBL, "action_button_test");
        FUNCTIONS.put(0x0041A21EL, "refresh_game_input");
        FUNCTIONS.put(0x0041AB67L, "test_direction_input_source");
        FUNCTIONS.put(0x0041AB74L, "test_action_input_source");
        FUNCTIONS.put(0x00676578L, "get_input_context");
        FUNCTIONS.put(0x005FE771L, "direction_repeat_update");
        FUNCTIONS.put(0x005FE8CFL, "cursor_move_dispatch");
        FUNCTIONS.put(0x005FE91BL, "cursor_move");
        FUNCTIONS.put(0x005F7979L, "condor_state_initialize");
        FUNCTIONS.put(0x005F824AL, "condor_ui_render_dispatch");
        FUNCTIONS.put(0x005FB754L, "condor_active_play_ui_update");
        FUNCTIONS.put(0x005FC2CAL, "return_to_cursor_mode");
        FUNCTIONS.put(0x005FCE95L, "modal_state_2_update");
        FUNCTIONS.put(0x005FD832L, "open_selected_unit_menu");
        FUNCTIONS.put(0x005FD8C7L, "close_selected_unit_menu");
        FUNCTIONS.put(0x005FCB75L, "modal_state_9_update");
        FUNCTIONS.put(0x005FCD6DL, "modal_state_10_update");
        FUNCTIONS.put(0x0060378BL, "open_setting_menu_and_select_unit_type");
        FUNCTIONS.put(0x00604208L, "modal_state_7_update");
        FUNCTIONS.put(0x006046A7L, "modal_state_8_update");
        FUNCTIONS.put(0x0060484BL, "modal_state_16_update");
        FUNCTIONS.put(0x006029FDL, "unit_hit_test");
        FUNCTIONS.put(0x00603230L, "unit_command_dispatch");
        FUNCTIONS.put(0x005F4273L, "condor_resource_load_root");
        FUNCTIONS.put(0x005F2F46L, "load_condor_archive_entry");
        FUNCTIONS.put(0x005F33B4L, "load_data_bin");
        FUNCTIONS.put(0x005F31DAL, "load_camera_bin");
        FUNCTIONS.put(0x005F3378L, "load_id_bin");
        FUNCTIONS.put(0x005F33DCL, "load_help_texture");
        FUNCTIONS.put(0x005F3160L, "parse_id_bin");
        FUNCTIONS.put(0x005F704CL, "consume_loaded_help_texture");
        FUNCTIONS.put(0x005F2BF0L, "initialize_loaded_condor_data");
        FUNCTIONS.put(0x005FF740L, "data_bin_consumer_ff740");
        FUNCTIONS.put(0x00601960L, "data_bin_consumer_601960");
        FUNCTIONS.put(0x006036BFL, "data_bin_consumer_6036bf");
        FUNCTIONS.put(0x00606B41L, "data_bin_consumer_606b41");
        FUNCTIONS.put(0x00609420L, "data_bin_consumer_609420");

        GLOBALS.put(0x00C72E80L, "condor_input_mask_current");
        GLOBALS.put(0x00C74C54L, "condor_input_pressed_edges");
        GLOBALS.put(0x00C74C48L, "condor_input_repeat_edges");
        GLOBALS.put(0x00C74C4CL, "condor_input_mask_previous");
        GLOBALS.put(0x00C74C50L, "condor_interaction_mode");
        GLOBALS.put(0x009A85D4L, "game_input_mask_current");
        GLOBALS.put(0x009A85E0L, "game_input_mask_pressed_edges");
        GLOBALS.put(0x00C625E0L, "condor_modal_state");
        GLOBALS.put(0x00CBCCC0L, "condor_cursor_x");
        GLOBALS.put(0x00CBCCC2L, "condor_cursor_y");
        GLOBALS.put(0x00C75268L, "condor_destination_cursor_x");
        GLOBALS.put(0x00C7526AL, "condor_destination_cursor_y");
        GLOBALS.put(0x00C6097CL, "condor_selected_unit_index");
        GLOBALS.put(0x00CBC930L, "condor_selected_unit_menu_row");
        GLOBALS.put(0x00C752D4L, "condor_selected_unit_menu_row_count");
        GLOBALS.put(0x00C74CA8L, "condor_selected_unit_command_0");
        GLOBALS.put(0x00C74CB0L, "condor_selected_unit_command_1");
        GLOBALS.put(0x00C74CB8L, "condor_selected_unit_command_2");
        GLOBALS.put(0x00C75254L, "condor_setting_menu_rotation_index");
        GLOBALS.put(0x00C75264L, "condor_setting_menu_unit_count");
        GLOBALS.put(0x00C75278L, "condor_setting_menu_unit_ids");
        GLOBALS.put(0x00CBCCA0L, "condor_setting_menu_relative_row");
        GLOBALS.put(0x00C625D0L, "condor_setting_menu_scroll_position");
        GLOBALS.put(0x00C74CC8L, "condor_setting_menu_row_stride");
        GLOBALS.put(0x00C606F0L, "condor_data_bin_resident_base");
        GLOBALS.put(0x00C606F8L, "condor_camera_bin_resident_base");
        GLOBALS.put(0x00C60700L, "condor_help_texture_resident_base");
        GLOBALS.put(0x00C60704L, "condor_id_bin_resident_base");
        GLOBALS.put(0x00C60708L, "condor_dynamic_texture_0");
        GLOBALS.put(0x00C6070CL, "condor_dynamic_texture_1");
    }

    private DecompInterface decompiler;

    @Override
    public void run() throws Exception {
        println("CONDOR_INPUT_MENU_DATA_EVIDENCE program=" + currentProgram.getName());
        println("language=" + currentProgram.getLanguageID());

        decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            println("=== GLOBAL_XREFS ===");
            for (Map.Entry<Long, String> entry : GLOBALS.entrySet()) {
                printGlobalReferences(entry.getKey(), entry.getValue());
            }

            println("=== FOCUSED_DECOMPILATION ===");
            for (Map.Entry<Long, String> entry : FUNCTIONS.entrySet()) {
                printDecompile(entry.getKey(), entry.getValue());
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
            String functionName = function == null ? "<no-function>" : function.getName();
            String instructionText = instruction == null ? "<no-instruction>" : instruction.toString();
            println("  " + reference.getFromAddress() + " " + functionName + " " +
                reference.getReferenceType() + " " + instructionText);
        }
    }

    private void printDecompile(long rawAddress, String label) throws Exception {
        Address address = toAddr(rawAddress);
        Function function = getFunctionAt(address);
        if (function == null) {
            function = getFunctionContaining(address);
        }

        println(String.format("FUNCTION %s 0x%08X", label, rawAddress));
        if (function == null) {
            println("  missing");
            return;
        }

        println("  ghidra=" + function.getName() + "@" + function.getEntryPoint());
        DecompileResults result = decompiler.decompileFunction(function, 90, monitor);
        if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
            println(result.getDecompiledFunction().getC());
        }
        else {
            println("  decompileFailed=" + result.getErrorMessage());
        }
    }
}
