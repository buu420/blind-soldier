// Headless Ghidra evidence for the legacy FFVII field state used by Blind
// Soldier's Story, object, and live-position navigation readers.

import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.Map;
import java.util.Set;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.Listing;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class BlindSoldierFieldNavigationStateEvidence extends GhidraScript {
    private static final Map<String, Long> TARGETS = new LinkedHashMap<>();

    static {
        TARGETS.put("fieldId", 0x00CC15D0L);
        TARGETS.put("currentModelId", 0x00CC0DB2L);
        TARGETS.put("modelCount", 0x00CFF73EL);
        TARGETS.put("modelTablePointer", 0x00CFF738L);
        TARGETS.put("fieldObjectArray", 0x00CC1670L);
        TARGETS.put("eventTablePointer", 0x00CC0B60L);
        TARGETS.put("entityModelIdArray", 0x00CBFB70L);
    }

    @Override
    public void run() throws Exception {
        println("BLIND_SOLDIER_FIELD_NAVIGATION_STATE_EVIDENCE");
        println("program=" + currentProgram.getName());
        println("language=" + currentProgram.getLanguageID());

        Listing listing = currentProgram.getListing();
        int totalReferences = 0;
        int coreTargetsWithReferences = 0;
        for (Map.Entry<String, Long> target : TARGETS.entrySet()) {
            monitor.checkCancelled();
            Address address = toAddr(target.getValue());
            ReferenceIterator references =
                currentProgram.getReferenceManager().getReferencesTo(address);
            Set<String> functions = new LinkedHashSet<>();
            int count = 0;
            while (references.hasNext()) {
                monitor.checkCancelled();
                Reference reference = references.next();
                Address from = reference.getFromAddress();
                Instruction instruction = listing.getInstructionContaining(from);
                Function function = listing.getFunctionContaining(from);
                if (function != null) {
                    functions.add(function.getName() + "@" + function.getEntryPoint());
                }
                if (count < 8) {
                    println("reference=" + target.getKey() + "|" + from + "|" +
                        reference.getReferenceType() + "|" +
                        (instruction == null ? "<no instruction>" : instruction));
                }
                count++;
            }

            println("target=" + target.getKey() + "|" + address +
                "|referenceCount=" + count + "|functions=" + functions);
            totalReferences += count;
            if ((target.getKey().equals("fieldId") ||
                 target.getKey().equals("modelTablePointer") ||
                 target.getKey().equals("eventTablePointer")) && count > 0) {
                coreTargetsWithReferences++;
            }
        }

        println("totalReferences=" + totalReferences);
        if (coreTargetsWithReferences != 3) {
            throw new Exception(
                "Native field ID, model table, or event table evidence is missing.");
        }
    }
}
