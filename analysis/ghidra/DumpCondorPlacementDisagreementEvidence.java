// Focused, read-only evidence for the remaining disagreement between the
// Fort Condor placement predicate and the asynchronously sampled native flag.
//
// @category FF7.BlindSoldier

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;

public class DumpCondorPlacementDisagreementEvidence extends GhidraScript {
    private static final long[] FUNCTIONS = {
        0x005FD958L, // per-frame input/UI update; owns the flag lifetime
        0x005FE63CL, // full placement predicate
        0x006029FDL, // unit-under-cursor scan
        0x00602F7DL, // placement-footprint overlap scan
        0x00606F20L, // temporary candidate + overlap + terrain lookup
        0x0060A450L, // fixed-point angle from one point to another
        0x0060A4C6L, // angle-sum helper
        0x0060A550L, // point-in-polygon predicate
        0x0060A682L  // terrain-record lookup
    };

    private static final long[] GLOBALS = {
        0x00CBCC9CL, // frame-local placement flag
        0x00C60AD0L, // allied-count gate
        0x00C6097CL, // unit-under-cursor result
        0x00C625E8L, // terrain records
        0x00C60AA4L  // terrain record count
    };

    @Override
    public void run() throws Exception {
        println("CONDOR_PLACEMENT_DISAGREEMENT_EVIDENCE program=" + currentProgram.getName());
        println("language=" + currentProgram.getLanguageID());

        for (long global : GLOBALS) {
            printReferences(global);
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (long entry : FUNCTIONS) {
                printFunction(entry, decompiler);
            }
        }
        finally {
            decompiler.dispose();
        }
    }

    private void printReferences(long rawAddress) throws Exception {
        Address address = toAddr(rawAddress);
        List<Reference> references = new ArrayList<>();
        ReferenceIterator iterator = currentProgram.getReferenceManager().getReferencesTo(address);
        while (iterator.hasNext()) {
            references.add(iterator.next());
        }
        references.sort(Comparator.comparing(reference -> reference.getFromAddress().getOffset()));

        println(String.format("GLOBAL 0x%08X references=%d", rawAddress, references.size()));
        for (Reference reference : references) {
            monitor.checkCancelled();
            Instruction instruction = getInstructionContaining(reference.getFromAddress());
            Function function = getFunctionContaining(reference.getFromAddress());
            println("  " + reference.getFromAddress() + " "
                + (function == null ? "<no-function>" : function.getName()) + " "
                + reference.getReferenceType() + " "
                + (instruction == null ? "<no-instruction>" : instruction.toString()));
        }
    }

    private void printFunction(long rawEntry, DecompInterface decompiler) throws Exception {
        Function function = getFunctionAt(toAddr(rawEntry));
        if (function == null) {
            function = getFunctionContaining(toAddr(rawEntry));
        }

        println(String.format("FUNCTION 0x%08X", rawEntry));
        if (function == null) {
            println("  missing");
            return;
        }

        println("  ghidra=" + function.getName() + "@" + function.getEntryPoint());
        DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
        if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
            println(result.getDecompiledFunction().getC());
        }
        else {
            println("  decompileFailed=" + result.getErrorMessage());
        }
    }
}
