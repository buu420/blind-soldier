// Read-only evidence for Tiny Bronco sailing, shore landing and rotation.
// Run against an analyzed, installed ff7_en.exe. Keep the decompiled output
// private; this repository contains the extraction script, not game code.
import java.nio.file.Files;
import java.nio.file.Path;
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.DecompInterface;

public class DumpTinyBroncoLandingEvidence extends GhidraScript {
    @Override
    public void run() throws Exception {
        if (getScriptArgs().length != 1)
            throw new IllegalArgumentException("Provide private output path");
        StringBuilder output = new StringBuilder("Program: " + currentProgram.getName() + "\n");
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (long offset : new long[] {
                0x007667B2L, // Cancel release, disembark state and party placement
                0x0076667CL, // begin disembark
                0x007666FFL, // terrain under the boat
                0x0075079DL, // dispatch the landing probe
                0x00766417L, // restore committed position and probe 800 units
                0x007628B5L, // committed position
                0x00761EECL, // model rotation plus slide rotation
                0x00753D00L, // rotate the probe vector
                0x00751EFCL, // five contact points and fanned collision recovery
                0x0074CC07L, // surface selection
                0x0076085FL, // edge-inclusive triangle test
                0x0074CECAL, // sailing and landing terrain masks
                0x0076247DL, // place party at the accepted landing
                0x00762837L, // restore the boat
                0x0076420AL, // model update and rotation easing
                0x00761C07L, // ease model rotation toward facing
                0x00761DF5L, // ease the slide rotation
                0x0074EA48L, // movement and facing for each camera mode
                0x0074D3D1L, // camera modes zero and one
                0x00765F61L  // native world entrance dispatch
            }) {
                monitor.checkCancelled();
                var address = toAddr(offset);
                var function = getFunctionAt(address);
                if (function == null) {
                    disassemble(address);
                    function = createFunction(address, null);
                }
                if (function == null) throw new IllegalStateException("No function at " + address);
                var result = decompiler.decompileFunction(function, 90, monitor);
                if (!result.decompileCompleted() || result.getDecompiledFunction() == null)
                    throw new IllegalStateException("Could not decompile " + address + ": " + result.getErrorMessage());
                output.append("\nFUNCTION ").append(address).append("\n")
                    .append(result.getDecompiledFunction().getC());
            }
        } finally {
            decompiler.dispose();
        }
        Files.writeString(Path.of(getScriptArgs()[0]), output.toString());
        println("Wrote native Tiny Bronco landing evidence.");
    }
}
