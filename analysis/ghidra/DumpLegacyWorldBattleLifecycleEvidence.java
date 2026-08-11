// Ghidra headless evidence for the legacy world-map battle lifecycle.
//
// The accessibility host keys off the native current-module byte.  Dump every
// function that writes that byte so encounter entry, battle ownership, result
// handling, field entry, and process exit can be distinguished from evidence
// in the shipped executable rather than inferred from timing alone.

import java.util.LinkedHashSet;
import java.util.Set;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class DumpLegacyWorldBattleLifecycleEvidence extends GhidraScript {
    private static final long CURRENT_MODULE_GLOBAL = 0x00CBF9DCL;

    @Override
    public void run() throws Exception {
        Address moduleAddress = toAddr(CURRENT_MODULE_GLOBAL);
        println("LEGACY_WORLD_BATTLE_LIFECYCLE_EVIDENCE program=" +
            currentProgram.getName());
        println("  currentModuleGlobal=" + moduleAddress);

        Set<Function> writers = new LinkedHashSet<>();
        ReferenceIterator references =
            currentProgram.getReferenceManager().getReferencesTo(moduleAddress);
        while (references.hasNext()) {
            monitor.checkCancelled();
            Reference reference = references.next();
            if (!reference.getReferenceType().isWrite()) {
                continue;
            }

            Function writer = getFunctionContaining(reference.getFromAddress());
            println("  writeReference=" + reference.getFromAddress() +
                " type=" + reference.getReferenceType() +
                " function=" + (writer == null
                    ? "none"
                    : writer.getName() + " " + writer.getEntryPoint()));
            if (writer != null) {
                writers.add(writer);
            }
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (Function writer : writers) {
                monitor.checkCancelled();
                println("FUNCTION " + writer.getName() + " " +
                    writer.getEntryPoint());
                DecompileResults result =
                    decompiler.decompileFunction(writer, 90, monitor);
                if (result.decompileCompleted() &&
                    result.getDecompiledFunction() != null) {
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
