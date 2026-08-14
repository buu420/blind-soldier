// Ghidra headless evidence for the native Quit selector and world-player owner.

import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.Map;
import java.util.Set;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class DumpLegacyQuitAndMountedWorldEvidence extends GhidraScript {
    private static final Map<String, Long> GLOBALS = new LinkedHashMap<>();
    static {
        GLOBALS.put("quitSelection", 0x00DC0FA0L);
        GLOBALS.put("quitCompletion", 0x00DC0FB4L);
        GLOBALS.put("quitVisibleLatch", 0x00DC0FB8L);
        GLOBALS.put("worldPlayerEntityPointer", 0x00E3A7D0L);
    }

    @Override
    public void run() throws Exception {
        println("LEGACY_QUIT_AND_MOUNTED_WORLD_EVIDENCE program=" + currentProgram.getName());
        Set<Function> functions = new LinkedHashSet<>();
        for (Map.Entry<String, Long> entry : GLOBALS.entrySet()) {
            Address address = toAddr(entry.getValue());
            println("GLOBAL " + entry.getKey() + "=" + address);
            ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(address);
            while (references.hasNext()) {
                monitor.checkCancelled();
                Reference reference = references.next();
                Function function = getFunctionContaining(reference.getFromAddress());
                println("  reference=" + reference.getFromAddress() +
                    " type=" + reference.getReferenceType() +
                    " function=" + (function == null ? "none" : function.getName() + " " + function.getEntryPoint()));
                if (function != null && reference.getReferenceType().isWrite()) {
                    functions.add(function);
                }
            }
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (Function function : functions) {
                monitor.checkCancelled();
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
