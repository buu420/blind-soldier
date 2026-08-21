// Minimal, repeatable decompile for the Fort Condor placement geometry path.
// Kept separate from the broad evidence script so the exact call arguments and
// point test are not lost in headless-analysis output truncation.
//
// @category FF7.BlindSoldier

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;

public class DumpCondorPlacementGeometryExact extends GhidraScript {
    private static final long[] FUNCTIONS = {
        0x00602F7DL, // live-unit footprint scan
        0x00606F20L, // candidate builder and overlap/terrain call site
        0x0060A450L, // fixed-point direction helper
        0x0060A4C6L, // wrapped angle difference
        0x0060A550L, // native point-in-triangle test
        0x0060A682L  // terrain-record scan
    };

    @Override
    public void run() throws Exception {
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (long entry : FUNCTIONS) {
                Function function = getFunctionAt(toAddr(entry));
                println(String.format("FUNCTION 0x%08X", entry));
                if (function == null) {
                    println("missing");
                    continue;
                }

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
}
