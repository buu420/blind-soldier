// Repeatable static evidence for FFVII's legacy world-map movement loop.
//
// The auto-walk controller changes only the player's configured direction
// keys. This pass finds native functions that reference the live world-player
// pointer or world camera front, then decompiles the small set that also touch
// the player position/direction offsets. It is deliberately read-only.

import java.util.LinkedHashSet;
import java.util.Set;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class DumpWorldMapAutoWalkMovementEvidence extends GhidraScript {
    private static final long WORLD_PLAYER_POINTER = 0x00E3A7D0L;
    private static final long WORLD_CAMERA_FRONT = 0x00DFC484L;

    @Override
    public void run() throws Exception {
        println("WORLD_MAP_AUTO_WALK_MOVEMENT_EVIDENCE program=" + currentProgram.getName());
        Set<Function> functions = new LinkedHashSet<>();
        collect("worldPlayerPointer", WORLD_PLAYER_POINTER, functions);
        collect("worldCameraFront", WORLD_CAMERA_FRONT, functions);

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (Function function : functions) {
                monitor.checkCancelled();
                DecompileResults result = decompiler.decompileFunction(function, 90, monitor);
                if (!result.decompileCompleted() || result.getDecompiledFunction() == null) {
                    println("DECOMPILE_FAILED " + function.getName() + " " +
                        function.getEntryPoint() + " " + result.getErrorMessage());
                    continue;
                }

                String c = result.getDecompiledFunction().getC();
                // Retain functions that expose the player layout or camera/input
                // feedback. The native entity offsets are position +0x0c/+0x10/
                // +0x14, facing +0x40, direction +0x4c, model +0x50, speed +0x55.
                if (c.contains("0xe3a7d0") || c.contains("0xdfc484") ||
                    c.contains("+ 0xc)") || c.contains("+ 0x10") ||
                    c.contains("+ 0x14") || c.contains("+ 0x40") ||
                    c.contains("+ 0x4c") || c.contains("+ 0x55")) {
                    println("FUNCTION " + function.getName() + " " + function.getEntryPoint());
                    println(c);
                }
            }
        }
        finally {
            decompiler.dispose();
        }
    }

    private void collect(String label, long value, Set<Function> functions) throws Exception {
        Address address = toAddr(value);
        println("GLOBAL " + label + " " + address);
        ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(address);
        while (references.hasNext()) {
            monitor.checkCancelled();
            Reference reference = references.next();
            Function function = getFunctionContaining(reference.getFromAddress());
            println("  REF " + reference.getFromAddress() + " " + reference.getReferenceType() +
                " " + (function == null ? "none" : function.getName() + " " + function.getEntryPoint()));
            if (function != null) {
                functions.add(function);
            }
        }
    }
}
