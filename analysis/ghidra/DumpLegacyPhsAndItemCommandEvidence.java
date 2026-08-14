// Read-only evidence for the normal PHS screen and remaining Item command flow.

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

public class DumpLegacyPhsAndItemCommandEvidence extends GhidraScript {
    private static final Map<String, Long> GLOBALS = new LinkedHashMap<>();
    static {
        GLOBALS.put("itemCommandWidget", 0x00DD1A18L);
        GLOBALS.put("itemKeyItemsWidget", 0x00DD1AC0L);
        GLOBALS.put("itemArrangeWidget", 0x00DD1AF8L);
        GLOBALS.put("itemCustomArrangeWidget", 0x00DD1B30L);
        GLOBALS.put("phsPartyWidget", 0x00DCA118L);
    }

    @Override
    public void run() throws Exception {
        println("LEGACY_PHS_ITEM_COMMAND_EVIDENCE program=" + currentProgram.getName());
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
                if (!result.decompileCompleted() || result.getDecompiledFunction() == null) {
                    println("  decompileFailed=" + result.getErrorMessage());
                    continue;
                }

                String[] lines = result.getDecompiledFunction().getC().split("\\R");
                for (String line : lines) {
                    String lower = line.toLowerCase();
                    if (lower.contains("dca118") ||
                        lower.contains("dca11c") ||
                        lower.contains("dd1a18") ||
                        lower.contains("dd1ac0") ||
                        lower.contains("dd1af8") ||
                        lower.contains("dd1b30") ||
                        lower.contains("dd1b34") ||
                        lower.contains("dd1b44") ||
                        lower.contains("fun_006f4d") ||
                        lower.contains("fun_006f0d") ||
                        lower.contains("3dced917") ||
                        lower.contains("3dcf0d84") ||
                        lower.contains("3dcd0679")) {
                        println(line);
                    }
                }
            }
        }
        finally {
            decompiler.dispose();
        }
    }
}
