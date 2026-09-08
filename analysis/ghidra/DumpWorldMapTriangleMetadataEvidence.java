// Repeatable Ghidra evidence for FFVII's packed world-map terrain word.
//
// These three adjacent accessors are called after collision resolution copies
// the selected triangle's packed metadata into the player entity at +0x4a.
// Dumping the shipped executable rather than relying on an old file-format
// description settles which bit is the native chocobo-location flag.

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;

public class DumpWorldMapTriangleMetadataEvidence extends GhidraScript {
    private static final long[] ACCESSORS = {
        0x00762136L, // terrain id: packed & 0x1f
        0x00762162L, // location id: (packed >> 9) & 0x1f
        0x00762191L  // chocobo flag: (packed >> 15) & 1
    };

    @Override
    public void run() throws Exception {
        println("WORLD_MAP_TRIANGLE_METADATA_EVIDENCE program=" + currentProgram.getName());
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (long offset : ACCESSORS) {
                monitor.checkCancelled();
                Address address = toAddr(offset);
                Function function = getFunctionAt(address);
                if (function == null) {
                    throw new IllegalStateException(
                        "Required world-map metadata accessor is missing at " + address);
                }

                println("FUNCTION " + function.getName() + " " + function.getEntryPoint());
                for (Instruction instruction = currentProgram.getListing().getInstructionAt(address);
                     instruction != null && function.getBody().contains(instruction.getAddress());
                     instruction = instruction.getNext()) {
                    println("  " + instruction.getAddress() + "  " + instruction);
                }

                DecompileResults result = decompiler.decompileFunction(function, 60, monitor);
                if (!result.decompileCompleted() || result.getDecompiledFunction() == null) {
                    throw new IllegalStateException(
                        "Could not decompile " + function.getName() + ": " + result.getErrorMessage());
                }

                println(result.getDecompiledFunction().getC());
            }
        }
        finally {
            decompiler.dispose();
        }
    }
}
