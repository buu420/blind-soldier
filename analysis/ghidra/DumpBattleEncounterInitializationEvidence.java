// Read-only Ghidra evidence for battle actor and encounter initialization.

import java.util.LinkedHashSet;
import java.util.Set;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class DumpBattleEncounterInitializationEvidence extends GhidraScript {
    private static final long[] GLOBALS = {
        0x009AB0DCL, // live battle actor table
        0x009AB0E4L, // party actor 0 instance id
        0x009AB284L, // enemy actor 4 instance id
        0x009A8794L, // enemy scene-index records
        0x009AB0A0L, // battle context / formation block
        0x009A8762L  // battle layout
    };

    @Override
    public void run() throws Exception {
        println("BATTLE_ENCOUNTER_INITIALIZATION_EVIDENCE program=" + currentProgram.getName());
        Set<Function> functions = new LinkedHashSet<>();
        for (long raw : GLOBALS) {
            Address address = toAddr(raw);
            println("GLOBAL " + address);
            ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(address);
            while (references.hasNext()) {
                monitor.checkCancelled();
                Reference reference = references.next();
                Function function = getFunctionContaining(reference.getFromAddress());
                println("  reference=" + reference.getFromAddress() +
                    " type=" + reference.getReferenceType() +
                    " function=" + (function == null
                        ? "none"
                        : function.getName() + " " + function.getEntryPoint()));
                if (function != null) {
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
