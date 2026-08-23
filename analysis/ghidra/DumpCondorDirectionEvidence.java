// Read-only evidence pass for the Fort Condor Direction selector.
//
// The selector writes two fields at +0x34/+0x36 in a live unit record. This
// pass finds every instruction in module 9 that accesses either field and
// decompiles the containing function, so the visual meaning of the 33 native
// positions can be established without guessing from the label alone.
//
// @category FF7.BlindSoldier

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;

import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;

public class DumpCondorDirectionEvidence extends GhidraScript {
    private static final long MODULE_START = 0x005F0000L;
    private static final long MODULE_END = 0x00610000L;

    @Override
    public void run() throws Exception {
        println("CONDOR_DIRECTION_EVIDENCE program=" + currentProgram.getName());

        Address start = toAddr(MODULE_START);
        Address end = toAddr(MODULE_END);
        InstructionIterator instructions = currentProgram.getListing().getInstructions(start, true);
        Map<Address, Function> matches = new LinkedHashMap<>();

        while (instructions.hasNext()) {
            monitor.checkCancelled();
            Instruction instruction = instructions.next();
            if (instruction.getAddress().compareTo(end) >= 0) {
                break;
            }

            String text = instruction.toString().toLowerCase(Locale.ROOT);
            if (!text.contains("+ 0x34]") && !text.contains("+ 0x36]")) {
                continue;
            }

            Function function = getFunctionContaining(instruction.getAddress());
            println("ACCESS " + instruction.getAddress() + " " +
                (function == null ? "<no-function>" : function.getName() + "@" + function.getEntryPoint()) +
                " " + instruction);
            if (function != null) {
                matches.putIfAbsent(function.getEntryPoint(), function);
            }
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (Function function : matches.values()) {
                monitor.checkCancelled();
                println("FUNCTION " + function.getName() + "@" + function.getEntryPoint());
                DecompileResults result = decompiler.decompileFunction(function, 90, monitor);
                if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
                    println(result.getDecompiledFunction().getC());
                }
                else {
                    println("  decompileFailed=" + result.getErrorMessage());
                }
            }
        }
        finally {
            decompiler.dispose();
        }
    }
}
