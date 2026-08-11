// Read-only evidence for the native live enemy-slot mask.

import java.util.LinkedHashSet;
import java.util.Set;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class DumpBattleEnemyMaskWriters extends GhidraScript {
    @Override
    public void run() throws Exception {
        Address address = toAddr(0x009AB0BAL);
        println("BATTLE_ENEMY_MASK_WRITERS program=" + currentProgram.getName() + " global=" + address);
        Set<Function> writers = new LinkedHashSet<>();
        ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(address);
        while (references.hasNext()) {
            Reference reference = references.next();
            Function function = getFunctionContaining(reference.getFromAddress());
            println("reference=" + reference.getFromAddress() + " type=" +
                reference.getReferenceType() + " function=" +
                (function == null ? "none" : function.getName() + " " + function.getEntryPoint()));
            if (function != null && reference.getReferenceType().isWrite()) {
                writers.add(function);
            }
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (Function function : writers) {
                println("FUNCTION " + function.getName() + " " + function.getEntryPoint());
                DecompileResults result = decompiler.decompileFunction(function, 90, monitor);
                println(result.decompileCompleted() && result.getDecompiledFunction() != null
                    ? result.getDecompiledFunction().getC()
                    : "decompileFailed=" + result.getErrorMessage());
            }
        }
        finally {
            decompiler.dispose();
        }
    }
}
