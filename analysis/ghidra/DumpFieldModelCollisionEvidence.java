// Focused, read-only evidence for FFVII's live field-model collision state.
//
// @category FF7.BlindSoldier

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

import java.util.Comparator;
import java.util.LinkedHashSet;
import java.util.Set;
import java.util.TreeSet;

public class DumpFieldModelCollisionEvidence extends GhidraScript {
    private static final long EVENT_TABLE_POINTER = 0x00CC0B60L;
    private static final long COLLISION_DISABLED_OFFSET = 0x5FL;
    private static final long COLLISION_WIDTH_OFFSET = 0x72L;

    @Override
    public void run() throws Exception {
        Set<Function> candidates = new TreeSet<>(
            Comparator.comparingLong(function -> function.getEntryPoint().getOffset()));
        Address eventTable = toAddr(EVENT_TABLE_POINTER);
        ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(eventTable);
        while (references.hasNext()) {
            monitor.checkCancelled();
            Reference reference = references.next();
            Function function = getFunctionContaining(reference.getFromAddress());
            if (function != null &&
                containsScalar(function, COLLISION_DISABLED_OFFSET) &&
                containsScalar(function, COLLISION_WIDTH_OFFSET)) {
                candidates.add(function);
            }
        }

        println("FIELD_MODEL_COLLISION_EVIDENCE program=" + currentProgram.getName());
        println("language=" + currentProgram.getLanguageID());
        println("eventTablePointer=" + eventTable + " candidates=" + candidates.size());

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (Function function : candidates) {
                monitor.checkCancelled();
                println("FUNCTION " + function.getName() + "@" + function.getEntryPoint());
                DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
                if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
                    println(result.getDecompiledFunction().getC());
                }
                else {
                    println("decompileFailed=" + result.getErrorMessage());
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
                    if (object instanceof Scalar scalar &&
                        scalar.getUnsignedValue() == expected) {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
