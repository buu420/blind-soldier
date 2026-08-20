// Read-only focused evidence for the native ability-menu record counts.

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;

public class DumpAbilityMenuRecordLayout extends GhidraScript {
    private static final long[] TARGETS = {
        0x006d7245L, // builds Magic, Summon, and Enemy Skill record arrays
        0x006e09bbL, // Enemy Skill renderer
        0x006e0b73L  // Enemy Skill description renderer
    };

    @Override
    public void run() throws Exception {
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (long target : TARGETS) {
                Function function = getFunctionAt(toAddr(target));
                if (function == null) {
                    println("MISSING " + toAddr(target));
                    continue;
                }

                DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
                println("FUNCTION " + function.getName() + " " + function.getEntryPoint());
                if (!result.decompileCompleted() || result.getDecompiledFunction() == null) {
                    println("FAILED " + result.getErrorMessage());
                    continue;
                }

                println(result.getDecompiledFunction().getC());
            }
        }
        finally {
            decompiler.dispose();
        }
    }
}
