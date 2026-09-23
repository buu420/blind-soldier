// Read-only evidence for how the party walks the world map: which terrain each model
// may stand on, the five contact points and their radius, the fanned slide, the surface
// under each point, and the per-frame step. Run against an analyzed, installed
// ff7_en.exe with a private output path as the single script argument. Keep the
// decompiled output private; this repository contains the extraction script, not game
// code.
import java.nio.file.Files;
import java.nio.file.Path;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Instruction;

public class DumpWutaiWalkingEvidence extends GhidraScript {
    @Override
    public void run() throws Exception {
        if (getScriptArgs().length != 1)
            throw new IllegalArgumentException("Provide private output path");
        StringBuilder output = new StringBuilder("Program: " + currentProgram.getName() + "\n");
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (long offset : new long[] {
                0x0074CECAL, // terrain masks: models 0-2 0x721B6F83, 0x20006000 from a bridge
                0x00762136L, // terrain under the party, tested for 13 and 14 by the bridge rule
                0x00761735L, // current player model
                0x00751EFCL, // radius 0x15E for model 5, 0xC8 otherwise; the 160-unit fan
                0x007530B3L, // the five contact points of one sample
                0x0074CC07L, // surface under a point: lowest for 3 and 5, else nearest height
                0x0076085FL, // edge-inclusive triangle test
                0x0074EA48L, // 0x1E a frame on foot, diagonals three quarters
                0x00765F61L  // entrance dispatch on a change of walkmap script
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

            // The contact points themselves, as instructions: the radius argument is added to
            // and subtracted from X and Z in turn around the sample's centre.
            var contacts = getFunctionAt(toAddr(0x007530B3L));
            output.append("\nINSTRUCTIONS ").append(contacts.getEntryPoint()).append("\n");
            Instruction instruction = getInstructionAt(contacts.getEntryPoint());
            while (instruction != null && contacts.getBody().contains(instruction.getAddress())) {
                output.append("  ").append(instruction.getAddress()).append(": ").append(instruction).append("\n");
                instruction = getInstructionAfter(instruction);
            }
        } finally {
            decompiler.dispose();
        }
        Files.writeString(Path.of(getScriptArgs()[0]), output.toString());
        println("Wrote native world walking evidence.");
    }
}
