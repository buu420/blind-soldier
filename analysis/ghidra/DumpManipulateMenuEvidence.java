// Run against an analyzed ff7_en.exe with -noanalysis -readOnly. The sole argument
// is a private output file. Do not commit the generated decompilation.
import java.nio.file.Files;
import java.nio.file.Path;
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.DecompInterface;

public class DumpManipulateMenuEvidence extends GhidraScript {
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 1) throw new IllegalArgumentException("Expected a private output path.");
        StringBuilder output = new StringBuilder("Manipulate owner byte references\n");
        for (var reference : currentProgram.getReferenceManager().getReferencesTo(toAddr(0x00dc3c64))) {
            output.append(reference.getFromAddress()).append(" ")
                .append(currentProgram.getListing().getInstructionAt(reference.getFromAddress())).append("\n");
        }
        var decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (long address : new long[] {0x006d797c, 0x006d8c75, 0x006d8a71,
                    0x006d8b1e, 0x006e1f64, 0x0041963c, 0x005d0690}) {
                monitor.checkCancelled();
                var function = getFunctionAt(toAddr(address));
                if (function == null) throw new IllegalStateException("Missing analyzed function " + Long.toHexString(address));
                var result = decompiler.decompileFunction(function, 120, monitor);
                if (!result.decompileCompleted()) throw new IllegalStateException(result.getErrorMessage());
                output.append("\nFUNCTION ").append(function.getEntryPoint()).append("\n")
                    .append(result.getDecompiledFunction().getC());
            }
        } finally {
            decompiler.dispose();
        }
        Files.writeString(Path.of(args[0]), output.toString());
        println("Wrote Manipulate menu evidence to the requested private path.");
    }
}
