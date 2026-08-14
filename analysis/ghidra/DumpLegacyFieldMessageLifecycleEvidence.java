// Ghidra headless evidence for the legacy field MESSAGE opcode lifecycle.

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

public class DumpLegacyFieldMessageLifecycleEvidence extends GhidraScript {
    private static final long MESSAGE_OPCODE_FUNCTION = 0x00618DBDL;

    @Override
    public void run() throws Exception {
        Address address = toAddr(MESSAGE_OPCODE_FUNCTION);
        Function function = getFunctionAt(address);
        println("LEGACY_FIELD_MESSAGE_LIFECYCLE_EVIDENCE program=" + currentProgram.getName());
        println("  messageFunction=" +
            (function == null ? "none" : function.getName() + " " + function.getEntryPoint()));
        if (function == null) {
            return;
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            DecompileResults result = decompiler.decompileFunction(function, 60, monitor);
            if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
                println(result.getDecompiledFunction().getC());
            }
            else {
                println("  decompileFailed=" + result.getErrorMessage());
            }
        }
        finally {
            decompiler.dispose();
        }
    }
}
