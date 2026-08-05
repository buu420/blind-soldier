// Ghidra headless evidence for Blind Soldier's legacy field-ladder state.

import java.util.Arrays;
import java.util.LinkedHashSet;
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

public class DumpLegacyFieldLadderEvidence extends GhidraScript {
    private static final long EVENT_TABLE_GLOBAL = 0x00CC0B60L;
    private static final long[] STRUCT_CONSTANTS = {
        0x88L, 0x63L, 0x6eL, 0x70L, 0x7aL, 0x7cL, 0x80L, 0x84L
    };

    @Override
    public void run() throws Exception {
        println("LEGACY_FIELD_LADDER_EVIDENCE program=" + currentProgram.getName());
        println("  language=" + currentProgram.getLanguageID());
        Address global = currentProgram.getAddressFactory()
            .getDefaultAddressSpace()
            .getAddress(EVENT_TABLE_GLOBAL);
        println("  eventTableGlobal=" + global);

        Set<Function> functions = new LinkedHashSet<>();
        ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(global);
        while (references.hasNext()) {
            monitor.checkCancelled();
            Reference reference = references.next();
            Function function = getFunctionContaining(reference.getFromAddress());
            println("  reference=" + reference.getFromAddress() +
                " function=" + (function == null ? "none" : function.getName()));
            if (function != null) {
                functions.add(function);
            }
        }

        if (functions.isEmpty()) {
            println("  references=none");
            return;
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (Function function : functions) {
                monitor.checkCancelled();
                println("FUNCTION " + function.getName() + " " + function.getEntryPoint());
                printConstants(function);
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

    private void printConstants(Function function) {
        Set<Long> found = new LinkedHashSet<>();
        InstructionIterator instructions = currentProgram.getListing()
            .getInstructions(function.getBody(), true);
        while (instructions.hasNext()) {
            Instruction instruction = instructions.next();
            for (int operand = 0; operand < instruction.getNumOperands(); operand++) {
                for (Object object : instruction.getOpObjects(operand)) {
                    if (object instanceof Scalar) {
                        long value = ((Scalar)object).getUnsignedValue();
                        if (Arrays.stream(STRUCT_CONSTANTS).anyMatch(candidate -> candidate == value)) {
                            found.add(value);
                        }
                    }
                }
            }
        }

        for (long value : STRUCT_CONSTANTS) {
            println(String.format("  constant_0x%X=%s", value, found.contains(value)));
        }
    }
}
