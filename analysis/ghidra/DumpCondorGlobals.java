// Generic read-only direct-reference dump. Pass hexadecimal addresses after
// the script name. Used to identify every producer/consumer of modal menu data.
//
// @category FF7.BlindSoldier

import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;

public class DumpCondorGlobals extends GhidraScript {
    @Override
    public void run() throws Exception {
        println("CONDOR_GLOBAL_EVIDENCE program=" + currentProgram.getName());
        for (String argument : getScriptArgs()) {
            long raw = Long.parseUnsignedLong(argument.replaceFirst("^(0x|0X)", ""), 16);
            List<Reference> references = new ArrayList<>();
            ReferenceIterator iterator = currentProgram.getReferenceManager().getReferencesTo(toAddr(raw));
            while (iterator.hasNext()) {
                references.add(iterator.next());
            }
            references.sort(Comparator.comparing(r -> r.getFromAddress().getOffset()));
            println(String.format("GLOBAL 0x%08X references=%d", raw, references.size()));
            for (Reference reference : references) {
                monitor.checkCancelled();
                Function function = getFunctionContaining(reference.getFromAddress());
                Instruction instruction = getInstructionContaining(reference.getFromAddress());
                println("  " + reference.getFromAddress() + " " +
                    (function == null ? "<no-function>" : function.getName() + "@" + function.getEntryPoint()) +
                    " " + reference.getReferenceType() + " " +
                    (instruction == null ? "<no-instruction>" : instruction.toString()));
            }
        }
    }
}
