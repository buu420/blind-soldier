// Ghidra headless evidence for Blind Soldier's field story/object data extractor.

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

public class DumpFieldDataCoverageOpcodes extends GhidraScript {
    private static final long FIELD_INIT_EVENT = 0x0060BACFL;
    private static final long EXECUTE_OPCODE_CALL_OFFSET = 0x80L;
    private static final long EXECUTE_OPCODE_TABLE_OFFSET = 0x10DL;

    @Override
    public void run() throws Exception {
        println("FIELD_DATA_COVERAGE_OPCODE_EVIDENCE program=" + currentProgram.getName());
        println("  language=" + currentProgram.getLanguageID());

        // Optional explicit helpers keep follow-up coordinate/interaction
        // investigations repeatable without changing the default opcode pass.
        if (getScriptArgs().length > 0) {
            for (String helper : getScriptArgs()) {
                printFunctionAt(Long.decode(helper), "REQUESTED_HELPER");
            }
            return;
        }

        long executeOpcode = resolveRelativeCall(FIELD_INIT_EVENT + EXECUTE_OPCODE_CALL_OFFSET);
        long opcodeTable = Integer.toUnsignedLong(getInt(address(executeOpcode + EXECUTE_OPCODE_TABLE_OFFSET)));
        println(String.format("OPCODE_DISPATCH execute=0x%08X table=0x%08X", executeOpcode, opcodeTable));

        printOpcodeHandler(opcodeTable, 0x03, "REQEW");
        printOpcodeHandler(opcodeTable, 0x14, "IFUB");
        printOpcodeHandler(opcodeTable, 0x16, "IFSW");
        printOpcodeHandler(opcodeTable, 0x5B, "SMTRA");
        printOpcodeHandler(opcodeTable, 0x60, "MAPJUMP");
        printOpcodeHandler(opcodeTable, 0x6D, "IDLCK");
        printOpcodeHandler(opcodeTable, 0x81, "SETWORD");
        printOpcodeHandler(opcodeTable, 0x82, "BITON");
        printOpcodeHandler(opcodeTable, 0xC3, "OFST");
        printFunctionAt(0x0060F750L, "FIELD_BANK_READ");
        printFunctionAt(0x0060FA7DL, "FIELD_BANK_WRITE");
    }

    private long resolveRelativeCall(long callOffset) throws Exception {
        Address call = address(callOffset);
        if ((getByte(call) & 0xff) != 0xe8) {
            throw new IllegalStateException("Expected CALL at " + call);
        }
        int displacement = getInt(call.add(1));
        return callOffset + 5L + displacement;
    }

    private void printOpcodeHandler(long table, int opcode, String name) throws Exception {
        long handlerOffset = Integer.toUnsignedLong(getInt(address(table + (long)opcode * 4L)));
        println(String.format("OPCODE 0x%02X %s handler=0x%08X", opcode, name, handlerOffset));
        printFunction(handlerOffset);
    }

    private void printFunctionAt(long offset, String label) throws Exception {
        println(String.format("HELPER %s address=0x%08X", label, offset));
        printFunction(offset);
    }

    private void printFunction(long offset) throws Exception {
        Address entry = address(offset);
        Function function = getFunctionAt(entry);
        if (function == null) {
            disassemble(entry);
            function = createFunction(entry, null);
        }
        println("FUNCTION " + (function == null ? "none" : function.getName()) + " " + entry);
        if (function == null) {
            return;
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            DecompileResults result = decompiler.decompileFunction(function, 60, monitor);
            if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
                println(result.getDecompiledFunction().getC());
            }
            else {
                println("  decompileFailed=" + result.getErrorMessage());
            }
        }
        finally {
            decompiler.dispose();
        }
    }

    private Address address(long offset) {
        return currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(offset);
    }
}
