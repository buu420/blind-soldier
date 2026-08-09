// Headless Ghidra evidence for the legacy field-movie playback flag used by
// Blind Soldier's opening-video activity fallback.

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.Listing;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class BlindSoldierMovieStateEvidence extends GhidraScript {
    private static final long FIELD_MOVIE_ACTIVE = 0x00CC1638L;

    @Override
    public void run() throws Exception {
        Address target = toAddr(FIELD_MOVIE_ACTIVE);
        Listing listing = currentProgram.getListing();
        ReferenceIterator references =
            currentProgram.getReferenceManager().getReferencesTo(target);
        int count = 0;
        println("BLIND_SOLDIER_MOVIE_STATE_EVIDENCE");
        println("program=" + currentProgram.getName());
        println("target=" + target);
        while (references.hasNext()) {
            monitor.checkCancelled();
            Reference reference = references.next();
            Address from = reference.getFromAddress();
            Instruction instruction = listing.getInstructionContaining(from);
            Function function = listing.getFunctionContaining(from);
            println("reference=" + from + "|" + reference.getReferenceType() +
                "|" + (instruction == null ? "<no instruction>" : instruction) +
                "|" + (function == null ? "<no function>" :
                    function.getName() + "@" + function.getEntryPoint()));
            count++;
        }
        println("referenceCount=" + count);
        if (count == 0) {
            throw new Exception(
                "No native references found for the field-movie playback flag.");
        }
    }
}
