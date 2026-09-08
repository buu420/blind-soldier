import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

/**
 * Repeats the native input-path evidence used by the Junon parade alignment
 * assist. The selected functions cover field input setup, consumption, and the
 * per-frame rebuild from FFVII's live three-bank control mappings.
 */
public class DumpJunonParadeAssistInput extends GhidraScript {
    private static final long FIELD_INIT_EVENT = 0x0060BACFL;
    private static final long EXECUTE_OPCODE_CALL_OFFSET = 0x80L;
    private static final long EXECUTE_OPCODE_TABLE_OFFSET = 0x10DL;
    private static final int CLAMPED_SUBTRACT_OPCODE = 0x78;

    private static final long[] FUNCTIONS = {
        0x0060BACFL,
        0x00636C41L,
        0x0063BDA8L,
        0x006499F7L
    };

    @Override
    public void run() throws Exception {
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            println("JUNON_PARADE_ASSIST_INPUT program=" + currentProgram.getName());
            for (long offset : FUNCTIONS) {
                monitor.checkCancelled();
                Address address = toAddr(offset);
                Function function = getFunctionAt(address);
                if (function == null) {
                    function = getFunctionContaining(address);
                }

                println(String.format(
                    "FUNCTION 0x%08X %s",
                    offset,
                    function == null ? "none" : function.getName()));
                if (function == null) {
                    continue;
                }

                DecompileResults result = decompiler.decompileFunction(function, 60, monitor);
                if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
                    println(result.getDecompiledFunction().getC());
                } else {
                    println("DECOMPILE_FAILED " + result.getErrorMessage());
                }
            }

            long executeOpcode = resolveRelativeCall(
                FIELD_INIT_EVENT + EXECUTE_OPCODE_CALL_OFFSET);
            long opcodeTable = Integer.toUnsignedLong(
                getInt(toAddr(executeOpcode + EXECUTE_OPCODE_TABLE_OFFSET)));
            long subtractHandler = Integer.toUnsignedLong(
                getInt(toAddr(opcodeTable + CLAMPED_SUBTRACT_OPCODE * 4L)));
            println(String.format(
                "OPCODE 0x%02X MINUS! handler=0x%08X",
                CLAMPED_SUBTRACT_OPCODE,
                subtractHandler));
            printFunction(decompiler, subtractHandler);
        } finally {
            decompiler.dispose();
        }
    }

    private long resolveRelativeCall(long callOffset) throws Exception {
        Address call = toAddr(callOffset);
        if ((getByte(call) & 0xff) != 0xe8) {
            throw new IllegalStateException("Expected CALL at " + call);
        }

        return callOffset + 5L + getInt(call.add(1));
    }

    private void printFunction(DecompInterface decompiler, long offset) throws Exception {
        Address address = toAddr(offset);
        Function function = getFunctionAt(address);
        if (function == null) {
            disassemble(address);
            function = createFunction(address, null);
        }

        println(String.format(
            "FUNCTION 0x%08X %s",
            offset,
            function == null ? "none" : function.getName()));
        if (function == null) {
            return;
        }

        DecompileResults result = decompiler.decompileFunction(function, 60, monitor);
        if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
            println(result.getDecompiledFunction().getC());
        } else {
            println("DECOMPILE_FAILED " + result.getErrorMessage());
        }
    }
}
