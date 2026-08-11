// Targeted, read-only decompilation of battle actor setup/clear functions.

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;

public class DumpBattleActorSetupFunctions extends GhidraScript {
    private static final long[] FUNCTIONS = {
        0x0041CCB2L, // clears battle-owned globals, including live actors
        0x005CF650L, // builds live actor records
        0x005D0CA0L  // consumes native active actor masks
    };

    @Override
    public void run() throws Exception {
        println("BATTLE_ACTOR_SETUP_FUNCTIONS program=" + currentProgram.getName());
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (long raw : FUNCTIONS) {
                Function function = getFunctionAt(toAddr(raw));
                if (function == null) {
                    println("MISSING " + toAddr(raw));
                    continue;
                }
                println("FUNCTION " + function.getName() + " " + function.getEntryPoint());
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
