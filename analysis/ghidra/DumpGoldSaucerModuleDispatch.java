// Read-only helper for the Gold Saucer minigame investigation. For each
// hexadecimal address argument it lists every reference to that address,
// including data references, so a function that only appears inside a module
// dispatch table can still be located. It then dumps the pointer-sized table
// slots either side of each data reference so the table index, which is the
// module id, can be read directly.
//
// @category FF7.BlindSoldier

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class DumpGoldSaucerModuleDispatch extends GhidraScript {
    private static final int TableSlots = 24;

    @Override
    public void run() throws Exception {
        println("GOLD_SAUCER_DISPATCH_EVIDENCE program=" + currentProgram.getName());
        for (String argument : getScriptArgs()) {
            long raw = Long.parseUnsignedLong(argument.replaceFirst("^(0x|0X)", ""), 16);
            Address target = toAddr(raw);
            println(String.format("TARGET 0x%08X", raw));
            ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(target);
            boolean any = false;
            while (references.hasNext()) {
                Reference reference = references.next();
                Address from = reference.getFromAddress();
                Function owner = getFunctionContaining(from);
                println(String.format(
                    "  ref from=%s type=%s owner=%s",
                    from,
                    reference.getReferenceType(),
                    owner == null ? "(data)" : owner.getName() + "@" + owner.getEntryPoint()));
                any = true;
                if (owner != null) {
                    continue;
                }

                // A data reference from outside any function is almost always a
                // dispatch-table slot. Print the neighbouring slots so the caller
                // can count the index of this entry.
                for (int slot = -TableSlots; slot <= TableSlots; slot++) {
                    try {
                        Address entry = from.add((long) slot * 4);
                        int value = getInt(entry);
                        println(String.format(
                            "    slot%+03d %s -> 0x%08X%s",
                            slot,
                            entry,
                            value,
                            slot == 0 ? "   <== this target" : ""));
                    } catch (Exception ignored) {
                        // Out of the initialised block; nothing to report.
                    }
                }
            }

            if (!any) {
                println("  no references");
            }
        }
    }
}
