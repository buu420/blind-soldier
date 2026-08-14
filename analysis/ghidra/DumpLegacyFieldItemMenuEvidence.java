// Read-only evidence for the out-of-battle Item menu state machine.

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;

public class DumpLegacyFieldItemMenuEvidence extends GhidraScript {
    private static final long[] FUNCTIONS = {
        0x00714ef2L,
        0x00714fa3L,
        0x00715105L
    };

    @Override
    public void run() throws Exception {
        println("LEGACY_FIELD_ITEM_MENU_EVIDENCE program=" + currentProgram.getName());
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (long address : FUNCTIONS) {
                monitor.checkCancelled();
                Function function = getFunctionContaining(toAddr(address));
                if (function == null) {
                    println("MISSING_FUNCTION " + toAddr(address));
                    continue;
                }

                println("FUNCTION " + function.getName() + " " + function.getEntryPoint());
                DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
                if (!result.decompileCompleted() || result.getDecompiledFunction() == null) {
                    println("  decompileFailed=" + result.getErrorMessage());
                    continue;
                }

                String[] lines = result.getDecompiledFunction().getC().split("\\R");
                for (String line : lines) {
                    if (function.getEntryPoint().getOffset() != 0x00715105L ||
                        line.contains("dd19c8") ||
                        line.contains("dd1a18") ||
                        line.contains("dd1a2c") ||
                        line.contains("dd1a3c") ||
                        line.contains("dd1a48") ||
                        line.contains("dd1a50") ||
                        line.contains("dd1a54") ||
                        line.contains("dd1a64") ||
                        line.contains("dd1a88") ||
                        line.contains("dd1a8c") ||
                        line.contains("dd1ac0") ||
                        line.contains("dd1af8") ||
                        line.contains("dd1b30") ||
                        line.contains("dd1b34") ||
                        line.contains("dd1b44") ||
                        line.contains("dd1b54") ||
                        line.contains("dd1b6c") ||
                        line.contains("FUN_006f4d30") ||
                        line.contains("FUN_006f4db2") ||
                        line.contains("FUN_006f0d7d") ||
                        line.contains("0x3c23d70a") ||
                        line.contains("0x3dced917")) {
                        println(line);
                    }
                }

                if (function.getEntryPoint().getOffset() == 0x00715105L) {
                    printContext(lines, "iVar7 = DAT_00dd1ac0", "KEY_ITEM_SELECTION");
                    printContext(lines, "local_24 + 3", "ARRANGE_COMMAND_ROWS");
                    printContext(lines, "DAT_00dd1b34 * 0x13", "CUSTOM_ARRANGE_SELECTION");
                }
            }
        }
        finally {
            decompiler.dispose();
        }
    }

    private void printContext(String[] lines, String needle, String heading) {
        for (int index = 0; index < lines.length; index++) {
            if (!lines[index].contains(needle)) {
                continue;
            }
            println("CONTEXT " + heading);
            int start = Math.max(0, index - 14);
            int end = Math.min(lines.length - 1, index + 24);
            for (int line = start; line <= end; line++) {
                println(lines[line]);
            }
            return;
        }
    }
}
