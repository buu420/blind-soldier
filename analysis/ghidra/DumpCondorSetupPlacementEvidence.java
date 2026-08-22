// Focused, repeatable evidence for Fort Condor setup-phase funds, unit
// lifecycle, and the exact placement/occupancy predicate.
//
// @category FF7.BlindSoldier

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.symbol.Reference;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

public class DumpCondorSetupPlacementEvidence extends GhidraScript {
    private static final long[][] RANGES = {
        { 0x00CBC700L, 0x00CBC900L }, // funds, enemy counters, setup globals
        { 0x00C60A80L, 0x00C60B20L }, // allied counters and setup requests
        { 0x00CBCC80L, 0x00CBF000L }  // cursor and live unit array
    };

    private static final long[] FUNCTIONS = {
        0x005F7756L, // initialize session-level Condor state
        0x005F7818L, // settle Condor rewards / leave battle
        0x005F7893L, // top-level Condor state update
        0x005F7979L, // initialize one battle
        0x005F7E10L, // module tick and result transitions
        0x005FD958L, // input/UI coordinator
        0x005FE63CL, // complete world-cursor placement predicate
        0x005FE771L, // cursor input and mode routing
        0x005FE8CFL, // cursor delta preparation
        0x005FE91BL, // apply cursor delta and camera-relative clamp
        0x006029FDL, // direct unit-under-cursor hit test
        0x00602F7DL, // placement footprint overlap scan
        0x00603FB4L, // accept hire / close Setting Menu
        0x00604009L, // hire and place unit
        0x00604208L, // Setting Menu update
        0x00606F20L, // build placement candidate and test overlap/terrain
        0x00607123L, // initialize unit at cursor position
        0x00607517L, // allocate a live-unit slot
        0x00607570L, // remove unit / update side counts
        0x00607727L, // enemy wave spawning
        0x006091FCL, // build active-unit counters
        0x00609748L, // build gil and selected-price display
        0x0060A682L  // terrain point lookup
    };

    @Override
    public void run() throws Exception {
        println("CONDOR_SETUP_PLACEMENT_EVIDENCE program=" + currentProgram.getName());
        println("language=" + currentProgram.getLanguageID());

        Map<Function, List<String>> rangeReferences = collectRangeReferences();
        println("=== RANGE_REFERENCES ===");
        for (Map.Entry<Function, List<String>> entry : rangeReferences.entrySet()) {
            println("FUNCTION " + entry.getKey().getName() + "@" + entry.getKey().getEntryPoint());
            for (String line : entry.getValue()) {
                println("  " + line);
            }
        }

        Set<Function> functions = new LinkedHashSet<>();
        for (long raw : FUNCTIONS) {
            Function function = getFunctionAt(toAddr(raw));
            if (function == null) {
                function = getFunctionContaining(toAddr(raw));
            }
            if (function != null) {
                functions.add(function);
            }
        }
        functions.addAll(rangeReferences.keySet());

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            println("=== DECOMPILATION ===");
            for (Function function : functions) {
                monitor.checkCancelled();
                println("FUNCTION " + function.getName() + "@" + function.getEntryPoint());
                DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
                if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
                    println(result.getDecompiledFunction().getC());
                }
                else {
                    println("decompile failed: " + result.getErrorMessage());
                }
            }
        }
        finally {
            decompiler.dispose();
        }
    }

    private Map<Function, List<String>> collectRangeReferences() throws Exception {
        Map<Function, List<String>> result = new LinkedHashMap<>();
        List<Function> orderedFunctions = new ArrayList<>();

        for (MemoryBlock block : currentProgram.getMemory().getBlocks()) {
            if (!block.isExecute()) {
                continue;
            }

            Instruction instruction = getInstructionAt(block.getStart());
            if (instruction == null) {
                instruction = getInstructionAfter(block.getStart());
            }
            while (instruction != null && instruction.getAddress().compareTo(block.getEnd()) <= 0) {
                monitor.checkCancelled();
                Function function = getFunctionContaining(instruction.getAddress());
                if (function != null) {
                    for (Reference reference : instruction.getReferencesFrom()) {
                        Address target = reference.getToAddress();
                        if (target != null && inRange(target.getOffset())) {
                            if (!result.containsKey(function)) {
                                result.put(function, new ArrayList<>());
                                orderedFunctions.add(function);
                            }
                            result.get(function).add(instruction.getAddress() + " "
                                + reference.getReferenceType() + " -> " + target + " "
                                + instruction);
                        }
                    }
                }
                instruction = instruction.getNext();
            }
        }

        orderedFunctions.sort(Comparator.comparing(f -> f.getEntryPoint().getOffset()));
        Map<Function, List<String>> ordered = new LinkedHashMap<>();
        for (Function function : orderedFunctions) {
            List<String> lines = result.get(function);
            lines.sort(String::compareTo);
            ordered.put(function, lines);
        }
        return ordered;
    }

    private boolean inRange(long address) {
        for (long[] range : RANGES) {
            if (address >= range[0] && address < range[1]) {
                return true;
            }
        }
        return false;
    }
}
