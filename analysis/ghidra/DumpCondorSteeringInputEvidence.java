// Read-only evidence dump for Fort Condor cursor-steering input.
//
// This traces module 9 from its update function through FFVII's logical-input
// mapper to the DirectInput keyboard poll, records the live masks used for
// acknowledgement, and prints the three mapping-bank slots for all four
// directions. It makes no changes to the analyzed program.
//
// @category FF7.BlindSoldier

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

public class DumpCondorSteeringInputEvidence extends GhidraScript {
    private static final long MAPPING_BASE = 0x009A85E8L;
    private static final int MAPPING_BANK_STRIDE = 0x64;

    private static final Map<Long, String> FUNCTIONS = new LinkedHashMap<>();
    private static final Map<Long, String> GLOBALS = new LinkedHashMap<>();

    static {
        FUNCTIONS.put(0x005F4A47L, "condor_module_main_loop");
        FUNCTIONS.put(0x005FD958L, "condor_input_and_ui_update");
        FUNCTIONS.put(0x005FADD4L, "collect_condor_controls");
        FUNCTIONS.put(0x00676578L, "obtain_input_context");
        FUNCTIONS.put(0x005FADB6L, "test_shared_held_mask_wrapper");
        FUNCTIONS.put(0x0041AB67L, "test_shared_held_mask");
        FUNCTIONS.put(0x005FAFBBL, "test_shared_pressed_mask_wrapper");
        FUNCTIONS.put(0x0041AB74L, "test_shared_pressed_mask");
        FUNCTIONS.put(0x0041A21EL, "rebuild_logical_input_from_mapping_banks");
        FUNCTIONS.put(0x0041F39CL, "initialize_directinput_keyboard");
        FUNCTIONS.put(0x0041F4F0L, "acquire_directinput_keyboard");
        FUNCTIONS.put(0x0041F55EL, "poll_directinput_keyboard_state");
        FUNCTIONS.put(0x0041A7EFL, "set_default_logical_binding");
        FUNCTIONS.put(0x0041A96DL, "install_default_input_bindings");
        FUNCTIONS.put(0x006CE6ECL, "load_ff7input_cfg");
        FUNCTIONS.put(0x006CE665L, "save_ff7input_cfg");
        FUNCTIONS.put(0x005FE771L, "held_direction_repeat");
        FUNCTIONS.put(0x005FE8CFL, "cursor_move_dispatch");
        FUNCTIONS.put(0x005FE91BL, "cursor_move_and_camera_update");

        GLOBALS.put(0x009ADED4L, "directinput_interface");
        GLOBALS.put(0x009ADA90L, "directinput_keyboard_device");
        GLOBALS.put(0x009ADAE4L, "directinput_keyboard_state_256_bytes");
        GLOBALS.put(0x009A85E8L, "logical_input_mapping_table_3_by_25");
        GLOBALS.put(0x009A85D4L, "shared_logical_held_mask");
        GLOBALS.put(0x009A85E0L, "shared_logical_pressed_mask");
        GLOBALS.put(0x00C72E80L, "condor_held_mask");
        GLOBALS.put(0x00C74C4CL, "condor_previous_mask");
        GLOBALS.put(0x00C74C54L, "condor_rising_edge_mask");
        GLOBALS.put(0x00C74C48L, "condor_menu_repeat_mask");
        GLOBALS.put(0x00CBC7BCL, "condor_direction_repeat_counter");
    }

    private DecompInterface decompiler;

    @Override
    public void run() throws Exception {
        println("CONDOR_STEERING_INPUT_EVIDENCE program=" + currentProgram.getName());
        println("language=" + currentProgram.getLanguageID());
        println("imageBase=" + currentProgram.getImageBase());

        decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            printDirectionConstantsAndMappingSlots();

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

    private void printDirectionConstantsAndMappingSlots() throws Exception {
        println("=== DIRECTION_MAPPING_SLOTS ===");
        String[] names = { "up", "right", "down", "left" };
        int[] logicalIndices = { 12, 13, 14, 15 };
        int[] logicalBits = { 0x1000, 0x2000, 0x4000, 0x8000 };
        int[] defaultDikTokens = { 0x48, 0x4D, 0x50, 0x4B };
        Memory memory = currentProgram.getMemory();

        for (int direction = 0; direction < names.length; direction++) {
            println(String.format("DIRECTION %s logicalIndex=%d bit=0x%04X defaultDikToken=0x%02X",
                names[direction], logicalIndices[direction], logicalBits[direction],
                defaultDikTokens[direction]));
            for (int bank = 0; bank < 3; bank++) {
                long slot = MAPPING_BASE + (bank * MAPPING_BANK_STRIDE) +
                    (logicalIndices[direction] * 4L);
                Address address = toAddr(slot);
                int initializedValue;
                try {
                    initializedValue = memory.getInt(address);
                    println(String.format("  bank=%d address=0x%08X initializedToken=0x%08X",
                        bank, slot, initializedValue));
                }
                catch (Exception exception) {
                    println(String.format("  bank=%d address=0x%08X initializedToken=<unreadable:%s>",
                        bank, slot, exception.getMessage()));
                }
            }
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
