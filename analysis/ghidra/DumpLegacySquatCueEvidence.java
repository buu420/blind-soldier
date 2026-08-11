// Ghidra headless evidence for Blind Soldier's Wall Market squat cue state.

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

public class DumpLegacySquatCueEvidence extends GhidraScript {
    private static final long CURRENT_ENTITY_SCRIPT_IDS = 0x00CBF9E8L;
    private static final long CURRENT_ENTITY_SCRIPT_PRIORITIES = 0x00CC0B30L;
    private static final long CURRENT_KEY_INPUT = 0x00CC0DF0L;
    private static final long TEMPORARY_FIELD_BANK = 0x00CC14D0L;
    private static final long FIELD_INIT_EVENT = 0x0060BACFL;
    private static final long EXECUTE_OPCODE_CALL_OFFSET = 0x80L;
    private static final long EXECUTE_OPCODE_TABLE_OFFSET = 0x10DL;

    @Override
    public void run() throws Exception {
        println("LEGACY_SQUAT_CUE_EVIDENCE program=" + currentProgram.getName());
        println("  language=" + currentProgram.getLanguageID());

        Map<String, Set<Function>> functionsByGlobal = new LinkedHashMap<>();
        functionsByGlobal.put("currentEntityScriptIds", findReferences(CURRENT_ENTITY_SCRIPT_IDS));
        functionsByGlobal.put("currentEntityScriptPriorities", findReferences(CURRENT_ENTITY_SCRIPT_PRIORITIES));
        functionsByGlobal.put("currentKeyInput", findReferences(CURRENT_KEY_INPUT));
        functionsByGlobal.put("temporaryFieldBank", findReferences(TEMPORARY_FIELD_BANK));

        Set<Function> executionStateReaders = new LinkedHashSet<>(
            functionsByGlobal.get("currentEntityScriptIds"));
        executionStateReaders.retainAll(functionsByGlobal.get("currentEntityScriptPriorities"));
        println("INTERSECTION scriptId+priority count=" + executionStateReaders.size());
        printFunctions(executionStateReaders);

        Set<Function> inputBankReaders = new LinkedHashSet<>(
            functionsByGlobal.get("currentKeyInput"));
        inputBankReaders.retainAll(functionsByGlobal.get("temporaryFieldBank"));
        println("INTERSECTION input+temporaryBank count=" + inputBankReaders.size());
        printFunctions(inputBankReaders);

        long executeOpcode = resolveRelativeCall(
            FIELD_INIT_EVENT + EXECUTE_OPCODE_CALL_OFFSET);
        long opcodeTable = Integer.toUnsignedLong(
            getInt(address(executeOpcode + EXECUTE_OPCODE_TABLE_OFFSET)));
        println(String.format(
            "OPCODE_DISPATCH execute=0x%08X table=0x%08X",
            executeOpcode,
            opcodeTable));
        printOpcodeHandler(opcodeTable, 0x30, "IFKEY");
        printOpcodeHandler(opcodeTable, 0x80, "SETBYTE");
        printOpcodeHandler(opcodeTable, 0x14, "IFUB");
        printOpcodeHandler(opcodeTable, 0x16, "IFSW");
        printFunctionAt(0x00612303L, "IFKEY_MASK_TEST");
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
        long handlerOffset = Integer.toUnsignedLong(
            getInt(address(table + (long)opcode * 4L)));
        Address handlerAddress = address(handlerOffset);
        Function handler = getFunctionAt(handlerAddress);
        if (handler == null) {
            disassemble(handlerAddress);
            handler = createFunction(handlerAddress, null);
        }
        println(String.format(
            "OPCODE 0x%02X %s handler=0x%08X function=%s",
            opcode,
            name,
            handlerOffset,
            handler == null ? "none" : handler.getName()));
        if (handler != null) {
            Set<Function> singleton = new LinkedHashSet<>();
            singleton.add(handler);
            printFunctions(singleton);
        }
    }

    private void printFunctionAt(long offset, String label) throws Exception {
        Function function = getFunctionAt(address(offset));
        println(String.format(
            "HELPER %s address=0x%08X function=%s",
            label,
            offset,
            function == null ? "none" : function.getName()));
        if (function != null) {
            Set<Function> singleton = new LinkedHashSet<>();
            singleton.add(function);
            printFunctions(singleton);
        }
    }

    private Address address(long offset) {
        return currentProgram.getAddressFactory()
            .getDefaultAddressSpace()
            .getAddress(offset);
    }

    private Set<Function> findReferences(long offset) throws Exception {
        Address address = currentProgram.getAddressFactory()
            .getDefaultAddressSpace()
            .getAddress(offset);
        Set<Function> functions = new LinkedHashSet<>();
        println(String.format("GLOBAL 0x%08X %s", offset, address));
        ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(address);
        while (references.hasNext()) {
            monitor.checkCancelled();
            Reference reference = references.next();
            Function function = getFunctionContaining(reference.getFromAddress());
            println("  reference=" + reference.getFromAddress() +
                " function=" + (function == null ? "none" : function.getName()));
            if (function != null) {
                functions.add(function);
            }
        }
        println("  functionCount=" + functions.size());
        return functions;
    }

    private void printFunctions(Set<Function> functions) throws Exception {
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (Function function : functions) {
                monitor.checkCancelled();
                println("FUNCTION " + function.getName() + " " + function.getEntryPoint());
                DecompileResults result = decompiler.decompileFunction(function, 60, monitor);
                if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
                    println(result.getDecompiledFunction().getC());
                }
                else {
                    println("  decompileFailed=" + result.getErrorMessage());
                }
            }
        }
        finally {
            decompiler.dispose();
        }
    }
}
