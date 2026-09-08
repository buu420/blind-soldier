// Read-only data-reference evidence for Gold Saucer accessibility research.
// @category FF7.BlindSoldier
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class DumpGoldSaucerDataReferences extends GhidraScript {
    @Override
    public void run() throws Exception {
        for (String argument : getScriptArgs()) {
            long value = Long.parseUnsignedLong(argument.replaceFirst("^(0x|0X)", ""), 16);
            println("DATA " + toAddr(value));
            ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(toAddr(value));
            while (references.hasNext()) {
                monitor.checkCancelled();
                Reference reference = references.next();
                Function function = getFunctionContaining(reference.getFromAddress());
                Instruction instruction = getInstructionAt(reference.getFromAddress());
                println(reference.getFromAddress() + " " + reference.getReferenceType() + " " +
                    (function == null ? "<no function>" : function.getEntryPoint()) + " " + instruction);
            }
        }
    }
}
