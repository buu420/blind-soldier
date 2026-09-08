// Read-only evidence dump for Fort Condor enemy spawning, route selection,
// movement speed, collision/engagement, and battle-wave selection.
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

public class DumpCondorPlacementStrategy extends GhidraScript {
    private static final Map<Long, String> FUNCTIONS = new LinkedHashMap<>();
    private static final Map<Long, String> GLOBALS = new LinkedHashMap<>();

    static {
        FUNCTIONS.put(0x005F7756L, "initialize_condor_session");
        FUNCTIONS.put(0x005F7979L, "initialize_condor_battle");
        FUNCTIONS.put(0x005FBD2FL, "update_condor_units_and_timers");
        FUNCTIONS.put(0x005FF33EL, "update_unit_animation_pass");
        FUNCTIONS.put(0x005FF38AL, "update_unit_simulation_pass");
        FUNCTIONS.put(0x005FC52BL, "derive_unit_motion_delta");
        FUNCTIONS.put(0x005FC684L, "derive_unit_motion_angle");
        FUNCTIONS.put(0x005FF6FCL, "schedule_live_unit_update");
        FUNCTIONS.put(0x005FF2E0L, "unit_can_acquire_or_engage");
        FUNCTIONS.put(0x005FF995L, "unit_animation_or_attack_dispatch");
        FUNCTIONS.put(0x005FFAB0L, "update_all_live_units");
        FUNCTIONS.put(0x005FFBD6L, "integrate_unit_position");
        FUNCTIONS.put(0x005FFCE9L, "acquire_opposing_unit");
        FUNCTIONS.put(0x005FFF45L, "update_unit_primary_state");
        FUNCTIONS.put(0x006001B2L, "dispatch_unit_advance");
        FUNCTIONS.put(0x00600216L, "dispatch_directed_advance");
        FUNCTIONS.put(0x00600247L, "advance_toward_route_or_destination");
        FUNCTIONS.put(0x0060048CL, "clear_unit_motion");
        FUNCTIONS.put(0x00600577L, "recover_unit_route");
        FUNCTIONS.put(0x00600648L, "route_recovery_step");
        FUNCTIONS.put(0x006006B6L, "route_recovery_fallback");
        FUNCTIONS.put(0x0060080BL, "resolve_combat_or_motion_target");
        FUNCTIONS.put(0x00600D9BL, "leave_combat_target");
        FUNCTIONS.put(0x00601960L, "calculate_attack_damage");
        FUNCTIONS.put(0x00602B91L, "find_opposing_unit_in_range");
        FUNCTIONS.put(0x00602F7DL, "placement_overlap_scan");
        FUNCTIONS.put(0x00604F67L, "attack_update_604f67");
        FUNCTIONS.put(0x006050A1L, "unit_progress_update");
        FUNCTIONS.put(0x00605B6EL, "start_unit_attack_state");
        FUNCTIONS.put(0x00605AA1L, "attack_update_605aa1");
        FUNCTIONS.put(0x00605C51L, "unit_state_dispatch");
        FUNCTIONS.put(0x0060619DL, "initialize_special_unit_motion");
        FUNCTIONS.put(0x006063AAL, "update_unselected_units");
        FUNCTIONS.put(0x0060683EL, "initialize_battle_units_or_geometry");
        FUNCTIONS.put(0x00606AA4L, "initialize_collision_route_cursor");
        FUNCTIONS.put(0x00606AFBL, "select_next_collision_route");
        FUNCTIONS.put(0x00606B41L, "install_route_velocity");
        FUNCTIONS.put(0x00606D3CL, "initialize_spawned_motion");
        FUNCTIONS.put(0x00606F20L, "test_placement_candidate");
        FUNCTIONS.put(0x00607123L, "initialize_live_unit");
        FUNCTIONS.put(0x0060747CL, "initialize_enemy_from_wave_entry");
        FUNCTIONS.put(0x00607727L, "spawn_enemy_wave_entry");
        FUNCTIONS.put(0x0060A77EL, "lookup_unit_collision_record");
        FUNCTIONS.put(0x0060A844L, "resolve_adjacent_collision_record");
        FUNCTIONS.put(0x0060A9AAL, "route_recovery_collision_test");

        GLOBALS.put(0x00DC0985L, "saved_enemy_wave_index");
        GLOBALS.put(0x00CBEDD8L, "active_enemy_wave_index");
        GLOBALS.put(0x00C606F0L, "resident_data_bin_base");
        GLOBALS.put(0x00CBCCD8L, "live_unit_array_base");
        GLOBALS.put(0x00C625E8L, "collision_record_array_base");
        GLOBALS.put(0x00C60AA4L, "collision_record_count");
        GLOBALS.put(0x00C60AE8L, "deployment_frontier_y");
        GLOBALS.put(0x00C752CCL, "motion_delta_x");
        GLOBALS.put(0x00C752D0L, "motion_delta_y");
        GLOBALS.put(0x00C752A8L, "motion_scale_or_timebase");
    }

    private DecompInterface decompiler;

    @Override
    public void run() throws Exception {
        println("CONDOR_PLACEMENT_STRATEGY_EVIDENCE program=" + currentProgram.getName());
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
        List<Reference> callers = new ArrayList<>();
        ReferenceIterator callerIterator = currentProgram.getReferenceManager().getReferencesTo(function.getEntryPoint());
        while (callerIterator.hasNext()) {
            Reference reference = callerIterator.next();
            if (reference.getReferenceType().isCall()) {
                callers.add(reference);
            }
        }
        callers.sort(Comparator.comparing(reference -> reference.getFromAddress().getOffset()));
        for (Reference caller : callers) {
            Function callerFunction = getFunctionContaining(caller.getFromAddress());
            println("  caller=" + caller.getFromAddress() + " " +
                (callerFunction == null ? "<no-function>" : callerFunction.getName()));
        }
        DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
        if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
            println(result.getDecompiledFunction().getC());
        }
        else {
            println("  decompileFailed=" + result.getErrorMessage());
        }
    }
}
