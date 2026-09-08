// Read-only helper. Lists every instruction that writes the FFVII module
// selector and, where the written value is an immediate constant, prints that
// constant with the enclosing function. This is how a minigame's module id is
// established from the installed executable instead of being guessed.
//
// @category FF7.BlindSoldier

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.scalar.Scalar;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class DumpModuleWriters extends GhidraScript {
    @Override
    public void run() throws Exception {
        println("MODULE_WRITER_EVIDENCE program=" + currentProgram.getName());
        for (String argument : getScriptArgs()) {
            long raw = Long.parseUnsignedLong(argument.replaceFirst("^(0x|0X)", ""), 16);
            Address target = toAddr(raw);
            println(String.format("SELECTOR 0x%08X", raw));
            ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(target);
            while (references.hasNext()) {
                Reference reference = references.next();
                if (!reference.getReferenceType().isWrite()) {
                    continue;
                }

                Address from = reference.getFromAddress();
                Instruction instruction = getInstructionAt(from);
                Function owner = getFunctionContaining(from);
                String immediate = "(not immediate)";
                if (instruction != null) {
                    for (int operand = 0; operand < instruction.getNumOperands(); operand++) {
                        Object[] parts = instruction.getOpObjects(operand);
                        for (Object part : parts) {
                            if (part instanceof Scalar scalar) {
                                immediate = String.format(
                                    "%d (0x%X)", scalar.getUnsignedValue(), scalar.getUnsignedValue());
                            }
                        }
                    }
                }

                println(String.format(
                    "  write at=%s value=%s owner=%s  %s",
                    from,
                    immediate,
                    owner == null ? "(none)" : owner.getName() + "@" + owner.getEntryPoint(),
                    instruction == null ? "" : instruction.toString()));
            }
        }
    }
}
