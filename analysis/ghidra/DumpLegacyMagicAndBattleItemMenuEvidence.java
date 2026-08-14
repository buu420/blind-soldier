// Read-only evidence for the native Magic category and Item menu widgets.

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

public class DumpLegacyMagicAndBattleItemMenuEvidence extends GhidraScript {
    private static final Map<String, Long> GLOBALS = new LinkedHashMap<>();
    static {
        GLOBALS.put("magicCategoryWidget", 0x00DD1698L);
        GLOBALS.put("fieldItemCommandWidget", 0x00DD1A18L);
        GLOBALS.put("fieldItemListWidget", 0x00DD1A50L);
        GLOBALS.put("fieldItemTargetWidget", 0x00DD1A88L);
        GLOBALS.put("fieldItemArrangeWidget", 0x00DD1AF8L);
        GLOBALS.put("savemapInventory", 0x00DC0234L);
        GLOBALS.put("battleItemCursorRow", 0x00DC20DCL);
        GLOBALS.put("battleItemScrollRow", 0x00DC20ECL);
        GLOBALS.put("battleItemRecords", 0x009AC354L);
    }

    @Override
    public void run() throws Exception {
        println("LEGACY_MAGIC_BATTLE_ITEM_EVIDENCE program=" + currentProgram.getName());
        println("  language=" + currentProgram.getLanguageID());

        Set<Function> functions = new LinkedHashSet<>();
        for (Map.Entry<String, Long> entry : GLOBALS.entrySet()) {
            monitor.checkCancelled();
            Address address = toAddr(entry.getValue());
            println("GLOBAL " + entry.getKey() + "=" + address);
            ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(address);
            int count = 0;
            while (references.hasNext()) {
                Reference reference = references.next();
                Function function = getFunctionContaining(reference.getFromAddress());
                println("  reference=" + reference.getFromAddress() +
                    " function=" + (function == null ? "none" : function.getName() + " " + function.getEntryPoint()));
                if (function != null) {
                    functions.add(function);
                }
                count++;
            }
            println("  referenceCount=" + count);
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (Function function : functions) {
                monitor.checkCancelled();
                println("FUNCTION " + function.getName() + " " + function.getEntryPoint());
                DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
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
