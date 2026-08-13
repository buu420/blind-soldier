// Ghidra headless evidence for FFVII's native ASK handler and FFNx voice wrapper.

import java.util.LinkedHashSet;
import java.util.Set;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class DumpLegacyAskEvidence extends GhidraScript {
    private static final long FIELD_INIT_EVENT = 0x0060BACFL;
    private static final long EXECUTE_OPCODE_CALL_OFFSET = 0x80L;
    private static final long EXECUTE_OPCODE_TABLE_OFFSET = 0x10DL;
    private static final int ASK_OPCODE = 0x48;
    private static final long ASK_UPDATE_CALL_OFFSET = 0x8EL;

    @Override
    public void run() throws Exception {
        Memory memory = currentProgram.getMemory();
        println("LEGACY_ASK_EVIDENCE program=" + currentProgram.getName());
        println("  language=" + currentProgram.getLanguageID());
        println("  imageBase=" + currentProgram.getImageBase());
        println("  min=" + memory.getMinAddress() + " max=" + memory.getMaxAddress());

        Address fieldInit = address(FIELD_INIT_EVENT);
        if (memory.contains(fieldInit) && (getByte(fieldInit.add(EXECUTE_OPCODE_CALL_OFFSET)) & 0xff) == 0xe8) {
            long executeOpcode = resolveRelativeCall(FIELD_INIT_EVENT + EXECUTE_OPCODE_CALL_OFFSET);
            long opcodeTable = Integer.toUnsignedLong(getInt(address(executeOpcode + EXECUTE_OPCODE_TABLE_OFFSET)));
            long askHandler = Integer.toUnsignedLong(getInt(address(opcodeTable + ASK_OPCODE * 4L)));
            println(String.format(
                "OPCODE_DISPATCH execute=0x%08X table=0x%08X ASK=0x%08X",
                executeOpcode,
                opcodeTable,
                askHandler));
            printFunctionAt(askHandler, "ASK_HANDLER");

            Address updateCall = address(askHandler + ASK_UPDATE_CALL_OFFSET);
            if (memory.contains(updateCall) && (getByte(updateCall) & 0xff) == 0xe8) {
                long updateHelper = resolveRelativeCall(askHandler + ASK_UPDATE_CALL_OFFSET);
                println(String.format("ASK_UPDATE_CALL call=0x%08X helper=0x%08X", askHandler + ASK_UPDATE_CALL_OFFSET, updateHelper));
                printFunctionAt(updateHelper, "ASK_CURSOR_HELPER");
                printCallers(updateHelper, "ASK_CURSOR_HELPER_CALLERS");
            }
            printCallers(askHandler, "ASK_HANDLER_CALLERS");
        }

        // FFNx opcode_voice_ask forwards to opcode_old_ask with this validated
        // tail. The pointer-slot bytes are wildcarded because relocations alter
        // them at load time.
        byte[] pattern = new byte[] {
            (byte)0xFF, 0x75, 0x08, (byte)0x88, 0x58, 0x08, (byte)0xA1,
            0, 0, 0, 0,
            (byte)0xFF, (byte)0xD0, (byte)0x83, (byte)0xC4, 0x04,
            0x5F, 0x5E, 0x5B, (byte)0x8B, (byte)0xE5, 0x5D, (byte)0xC3
        };
        byte[] masks = new byte[pattern.length];
        for (int index = 0; index < masks.length; index++) {
            masks[index] = (byte)(index >= 7 && index <= 10 ? 0x00 : 0xFF);
        }

        Address cursor = memory.getMinAddress();
        int matchCount = 0;
        while (cursor != null) {
            Address match = memory.findBytes(cursor, pattern, masks, true, monitor);
            if (match == null) {
                break;
            }
            matchCount++;
            long pointerSlot = Integer.toUnsignedLong(getInt(match.add(7)));
            println(String.format("FFNX_ASK_TAIL match=%s pointerSlot=0x%08X", match, pointerSlot));
            Function wrapper = getFunctionContaining(match);
            if (wrapper != null) {
                printFunction(wrapper, "FFNX_ASK_WRAPPER");
            }
            cursor = match.add(pattern.length);
        }
        println("FFNX_ASK_TAIL_COUNT=" + matchCount);
    }

    private long resolveRelativeCall(long callOffset) throws Exception {
        Address call = address(callOffset);
        if ((getByte(call) & 0xff) != 0xe8) {
            throw new IllegalStateException("Expected CALL at " + call);
        }
        int displacement = getInt(call.add(1));
        return callOffset + 5L + displacement;
    }

    private void printFunctionAt(long offset, String label) throws Exception {
        Address target = address(offset);
        Function function = getFunctionAt(target);
        if (function == null && currentProgram.getMemory().contains(target)) {
            disassemble(target);
            function = createFunction(target, null);
        }
        println(String.format("%s address=0x%08X function=%s", label, offset, function == null ? "none" : function.getName()));
        if (function != null) {
            printFunction(function, label);
        }
    }

    private void printCallers(long offset, String label) throws Exception {
        Set<Function> callers = new LinkedHashSet<>();
        ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(address(offset));
        while (references.hasNext()) {
            Reference reference = references.next();
            Function caller = getFunctionContaining(reference.getFromAddress());
            println(label + " reference=" + reference.getFromAddress() + " caller=" + (caller == null ? "none" : caller.getName()));
            if (caller != null) {
                callers.add(caller);
            }
        }
        for (Function caller : callers) {
            printFunction(caller, label);
        }
    }

    private void printFunction(Function function, String label) throws Exception {
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            println(label + " FUNCTION " + function.getName() + " " + function.getEntryPoint());
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
