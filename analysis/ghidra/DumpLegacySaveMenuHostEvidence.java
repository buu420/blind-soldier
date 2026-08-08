// Ghidra headless evidence for the legacy Save menu host/module relationship.

import java.util.LinkedHashSet;
import java.util.Set;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class DumpLegacySaveMenuHostEvidence extends GhidraScript {
    private static final long SAVE_MENU_FUNCTION = 0x006FEDB0L;
    private static final long CURRENT_MODULE_GLOBAL = 0x00CBF9DCL;

    @Override
    public void run() throws Exception {
        Address saveAddress = toAddr(SAVE_MENU_FUNCTION);
        Address moduleAddress = toAddr(CURRENT_MODULE_GLOBAL);
        Function save = getFunctionAt(saveAddress);
        println("LEGACY_SAVE_MENU_HOST_EVIDENCE program=" + currentProgram.getName());
        println("  saveFunction=" + (save == null ? "none" : save.getName() + " " + save.getEntryPoint()));
        println("  currentModuleGlobal=" + moduleAddress);
        if (save == null) {
            return;
        }

        Set<Function> callers = new LinkedHashSet<>();
        ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(saveAddress);
        while (references.hasNext()) {
            monitor.checkCancelled();
            Reference reference = references.next();
            Function caller = getFunctionContaining(reference.getFromAddress());
            println("  callerReference=" + reference.getFromAddress() +
                " caller=" + (caller == null ? "none" : caller.getName() + " " + caller.getEntryPoint()));
            if (caller != null) {
                callers.add(caller);
            }
        }

        println("  saveReadsCurrentModule=" + referencesGlobal(save, moduleAddress));
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            decompile(decompiler, save);
            for (Function caller : callers) {
                monitor.checkCancelled();
                println("  callerReadsCurrentModule=" + caller.getName() + ":" +
                    referencesGlobal(caller, moduleAddress));
                decompile(decompiler, caller);
            }
        }
        finally {
            decompiler.dispose();
        }
    }

    private boolean referencesGlobal(Function function, Address global) {
        ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(global);
        while (references.hasNext()) {
            Reference reference = references.next();
            if (function.getBody().contains(reference.getFromAddress())) {
                return true;
            }
        }
        return false;
    }

    private void decompile(DecompInterface decompiler, Function function) {
        println("FUNCTION " + function.getName() + " " + function.getEntryPoint());
        DecompileResults result = decompiler.decompileFunction(function, 60, monitor);
        if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
            println(result.getDecompiledFunction().getC());
        }
        else {
            println("  decompileFailed=" + result.getErrorMessage());
        }
    }
}
