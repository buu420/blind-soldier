// Read-only evidence for world entity lifetime and Tiny Bronco boarding contact.
// Run after importing/analyzing the user's installed ff7_en.exe. Output remains
// private: the project ships this script, not executable bytes or decompiled code.
import java.nio.file.Files;
import java.nio.file.Path;
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.DecompInterface;

public class DumpTinyBroncoTransportationEvidence extends GhidraScript {
    @Override
    public void run() throws Exception {
        StringBuilder output = new StringBuilder("Program: " + currentProgram.getName() + "\n");
        for (int model : new int[] {0, 1, 2, 5, 6}) {
            byte[] mask = getBytes(toAddr(0x0096DDB0L + model * 8), 8);
            output.append("MODEL ").append(model).append(" MASK");
            for (byte value : mask) output.append(String.format(" %02X", value & 0xff));
            output.append("\n");
        }
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (long offset : new long[] {
                0x007610B3L, // allocate and link entity
                0x007611AEL, // attach party to boarded vehicle
                0x00761313L, // detach party and restore player ownership
                0x00762993L, // find collision and store interacting entity at +4
                0x00762A21L, // native mask contact and movement rejection
                0x0076420AL, // dispatch model action from contact pointer and Confirm
                0x007667B2L  // native disembark handling
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
        if (getScriptArgs().length != 1) throw new IllegalArgumentException("Provide private output path");
        Files.writeString(Path.of(getScriptArgs()[0]), output.toString());
        println("Wrote native Tiny Bronco evidence.");
    }
}
