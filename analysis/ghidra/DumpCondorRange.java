// Read-only helper for compact instruction evidence around a Fort Condor
// branch or state write. Pass hexadecimal start and end addresses.
//
// @category FF7.BlindSoldier

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;

public class DumpCondorRange extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] arguments = getScriptArgs();
        if (arguments.length != 2) {
            throw new IllegalArgumentException("Expected hexadecimal start and end addresses");
        }

        Address start = toAddr(parse(arguments[0]));
        Address end = toAddr(parse(arguments[1]));
        println("CONDOR_RANGE_EVIDENCE program=" + currentProgram.getName() +
            " start=" + start + " end=" + end);
        for (Instruction instruction : currentProgram.getListing().getInstructions(start, true)) {
            monitor.checkCancelled();
            if (instruction.getAddress().compareTo(end) > 0) {
                break;
            }
            Function function = getFunctionContaining(instruction.getAddress());
            println(instruction.getAddress() + " " +
                (function == null ? "<no-function>" : function.getName()) + " " + instruction);
        }
    }

    private long parse(String value) {
        return Long.parseUnsignedLong(value.replaceFirst("^(0x|0X)", ""), 16);
    }
}
