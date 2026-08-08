// Ghidra headless evidence for the legacy Config, Limit, and Order menu state.

import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.Map;
import java.util.Set;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;
import ghidra.program.model.scalar.Scalar;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class DumpLegacyMenuAccessibilityEvidence extends GhidraScript {
    private static final Map<String, Long> GLOBALS = new LinkedHashMap<>();
    static {
        GLOBALS.put("configRow", 0x00DC10F0L);
        GLOBALS.put("limitCommandWidget", 0x00DCA1D0L);
        GLOBALS.put("limitLevelWidget", 0x00DCA198L);
        GLOBALS.put("limitMoveWidget", 0x00DCA240L);
        GLOBALS.put("orderPartyCursor", 0x00DC11C4L);
        GLOBALS.put("orderSelectionLatch", 0x00DC1320L);
    }

    @Override
    public void run() throws Exception {
        println("LEGACY_MENU_ACCESSIBILITY_EVIDENCE program=" + currentProgram.getName());
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

        Function orderBlock = getFunctionContaining(toAddr(0x006CA4C0L));
        if (orderBlock != null) {
            functions.add(orderBlock);
            println("ORDER_BLOCK function=" + orderBlock.getName() + " " + orderBlock.getEntryPoint());
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (Function function : functions) {
                monitor.checkCancelled();
                println("FUNCTION " + function.getName() + " " + function.getEntryPoint());
                println("  containsCharacterRowOffset0x1F=" + containsScalar(function, 0x1fL));
                DecompileResults result = decompiler.decompileFunction(function, 60, monitor);
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

    private boolean containsScalar(Function function, long expected) {
        InstructionIterator instructions = currentProgram.getListing()
            .getInstructions(function.getBody(), true);
        while (instructions.hasNext()) {
            Instruction instruction = instructions.next();
            for (int operand = 0; operand < instruction.getNumOperands(); operand++) {
                for (Object object : instruction.getOpObjects(operand)) {
                    if (object instanceof Scalar &&
                        ((Scalar)object).getUnsignedValue() == expected) {
                        return true;
                    }
                }
            }
        }
        return false;
    }
}
