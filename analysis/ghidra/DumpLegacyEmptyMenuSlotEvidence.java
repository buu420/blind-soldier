// Read-only evidence for empty Item, Magic, Summon, and Enemy Skill slots.

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

public class DumpLegacyEmptyMenuSlotEvidence extends GhidraScript {
    private static final Map<String, Long> GLOBALS = new LinkedHashMap<>();
    static {
        GLOBALS.put("magicWidget", 0x00DD1708L);
        GLOBALS.put("summonWidget", 0x00DD1740L);
        GLOBALS.put("enemySkillWidget", 0x00DD1778L);
        GLOBALS.put("magicRecords", 0x00DBA5A0L);
        GLOBALS.put("summonRecords", 0x00DBA760L);
        GLOBALS.put("enemySkillRecords", 0x00DBA7E0L);
        GLOBALS.put("savemapInventory", 0x00DC0234L);
        GLOBALS.put("battleItemRecords", 0x009AC354L);
    }

    @Override
    public void run() throws Exception {
        println("LEGACY_EMPTY_MENU_SLOT_EVIDENCE program=" + currentProgram.getName());
        Set<Function> functions = new LinkedHashSet<>();
        for (Map.Entry<String, Long> entry : GLOBALS.entrySet()) {
            Address address = toAddr(entry.getValue());
            println("GLOBAL " + entry.getKey() + "=" + address);
            ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(address);
            while (references.hasNext()) {
                Reference reference = references.next();
                Function function = getFunctionContaining(reference.getFromAddress());
                println("  reference=" + reference.getFromAddress() +
                    " function=" + (function == null ? "none" : function.getName() + " " + function.getEntryPoint()));
                if (function != null && (
                    function.getEntryPoint().getOffset() == 0x00710dfaL ||
                    function.getEntryPoint().getOffset() == 0x00715105L ||
                    function.getEntryPoint().getOffset() == 0x005d1520L ||
                    function.getEntryPoint().getOffset() == 0x006df007L ||
                    function.getEntryPoint().getOffset() == 0x006debfeL)) {
                    functions.add(function);
                }
            }
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (Function function : functions) {
                DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
                println("FUNCTION " + function.getName() + " " + function.getEntryPoint());
                if (!result.decompileCompleted() || result.getDecompiledFunction() == null) {
                    println("  decompileFailed=" + result.getErrorMessage());
                    continue;
                }

                String[] lines = result.getDecompiledFunction().getC().split("\\R");
                for (int index = 0; index < lines.length; index++) {
                    String lower = lines[index].toLowerCase();
                    if (lower.contains("dba5a0") || lower.contains("dba760") ||
                        lower.contains("dba7e0") || lower.contains("dd1708") ||
                        lower.contains("dd1740") || lower.contains("dd1778") ||
                        lower.contains("dc0234") || lower.contains("9ac354") ||
                        lower.contains("0xffff") || lower.contains("!= -1") ||
                        lower.contains("== -1")) {
                        int first = Math.max(0, index - 3);
                        int last = Math.min(lines.length - 1, index + 3);
                        for (int line = first; line <= last; line++) {
                            println(String.format("  %04d %s", line + 1, lines[line]));
                        }
                        println("  ---");
                    }
                }
            }
        }
        finally {
            decompiler.dispose();
        }
    }
}
