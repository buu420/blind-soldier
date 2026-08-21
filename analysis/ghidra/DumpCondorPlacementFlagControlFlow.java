// Focused excerpts from the module-9 update routine around the placement flag,
// report state, and validator call. This keeps the evidence readable while
// retaining enough surrounding control flow to explain stale async samples.
//
// @category FF7.BlindSoldier

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;

import java.util.Set;
import java.util.TreeSet;

public class DumpCondorPlacementFlagControlFlow extends GhidraScript {
    @Override
    public void run() throws Exception {
        Function function = getFunctionAt(toAddr(0x005FD958L));
        if (function == null) {
            println("FUN_005FD958 missing");
            return;
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
            String[] lines = result.getDecompiledFunction().getC().split("\\R");
            Set<Integer> selected = new TreeSet<>();
            String[] needles = {
                "DAT_00c72dec", "DAT_00cbcc9c", "FUN_005fe63c",
                "DAT_00c625e0", "DAT_00c6097c"
            };
            for (int index = 0; index < lines.length; index++) {
                for (String needle : needles) {
                    if (lines[index].contains(needle)) {
                        for (int context = Math.max(0, index - 6);
                             context <= Math.min(lines.length - 1, index + 6);
                             context++) {
                            selected.add(context);
                        }
                    }
                }
            }

            int previous = -2;
            for (int index : selected) {
                if (index != previous + 1) {
                    println("...");
                }
                println(String.format("%04d %s", index + 1, lines[index]));
                previous = index;
            }
        }
        finally {
            decompiler.dispose();
        }
    }
}
