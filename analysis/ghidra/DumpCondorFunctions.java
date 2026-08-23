// Generic read-only helper used by the Fort Condor modal-state investigation.
// Pass one or more hexadecimal function addresses after the script name.
//
// @category FF7.BlindSoldier

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;

public class DumpCondorFunctions extends GhidraScript {
    @Override
    public void run() throws Exception {
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            println("CONDOR_FUNCTION_EVIDENCE program=" + currentProgram.getName());
            for (String argument : getScriptArgs()) {
                long raw = Long.parseUnsignedLong(argument.replaceFirst("^(0x|0X)", ""), 16);
                Function function = getFunctionAt(toAddr(raw));
                if (function == null) {
                    function = getFunctionContaining(toAddr(raw));
                }
                println(String.format("FUNCTION requested=0x%08X", raw));
                if (function == null) {
                    println("  missing");
                    continue;
                }

                println("  resolved=" + function.getName() + "@" + function.getEntryPoint());
                println("  callers:");
                List<Reference> callers = new ArrayList<>();
                ReferenceIterator references = currentProgram.getReferenceManager()
                    .getReferencesTo(function.getEntryPoint());
                while (references.hasNext()) {
                    Reference reference = references.next();
                    if (reference.getReferenceType().isCall()) {
                        callers.add(reference);
                    }
                }
                callers.sort(Comparator.comparing(r -> r.getFromAddress().getOffset()));
                for (Reference reference : callers) {
                    Function caller = getFunctionContaining(reference.getFromAddress());
                    println("    " + reference.getFromAddress() + " " +
                        (caller == null ? "<no-function>" : caller.getName() + "@" + caller.getEntryPoint()));
                }

                println("  instructions:");
                for (Instruction instruction : currentProgram.getListing()
                        .getInstructions(function.getBody(), true)) {
                    monitor.checkCancelled();
                    println("    " + instruction.getAddress() + " " + instruction);
                }

                println("  decompile:");
                DecompileResults result = decompiler.decompileFunction(function, 90, monitor);
                if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
                    println(result.getDecompiledFunction().getC());
                }
                else {
                    println("    failed=" + result.getErrorMessage());
                }
            }
        }
        finally {
            decompiler.dispose();
        }
    }
}
