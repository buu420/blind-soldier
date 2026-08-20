// Read-only Ghidra evidence for the legacy Midgar Zolom world-map actor.
//
// Dump the native snake update/AI routines, the player-versus-world-model
// collision routine, and all code references to the snake position globals.
// Blind Soldier uses this evidence to expose the same crossing opportunity a
// sighted player gets from watching the Zolom move through the marsh.

import java.util.LinkedHashSet;
import java.util.Set;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class DumpLegacyMidgarZolomEvidence extends GhidraScript {
    private static final long[] FUNCTIONS = {
        0x00756138L, // initialize snake position history
        0x007561F6L, // snake/player relation used by world scripts
        0x007562A9L, // is_update_snake_enabled
        0x007562FFL, // run_world_snake_ai_script
        0x007564CDL, // update_world_snake_position
        0x0075692AL, // animate snake position history
        0x0076296EL  // current-entity collision with other world models
    };

    private static final long[] GLOBALS = {
        0x00E2A18CL, // active snake position pointer
        0x00E29F80L, // snake position history start
        0x00E2A100L  // snake position history end
    };

    @Override
    public void run() throws Exception {
        println("LEGACY_MIDGAR_ZOLOM_EVIDENCE program=" +
            currentProgram.getName());

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            Set<Function> functions = new LinkedHashSet<>();
            for (long raw : FUNCTIONS) {
                Address address = toAddr(raw);
                Function function = getFunctionAt(address);
                if (function == null) {
                    function = getFunctionContaining(address);
                }
                println("FUNCTION_REQUEST address=" + address + " resolved=" +
                    (function == null ? "none" : function.getName() + " " +
                        function.getEntryPoint()));
                if (function != null) {
                    functions.add(function);
                }
            }

            for (long raw : GLOBALS) {
                Address address = toAddr(raw);
                println("GLOBAL address=" + address);
                ReferenceIterator references =
                    currentProgram.getReferenceManager().getReferencesTo(address);
                while (references.hasNext()) {
                    monitor.checkCancelled();
                    Reference reference = references.next();
                    Function function =
                        getFunctionContaining(reference.getFromAddress());
                    println("  reference=" + reference.getFromAddress() +
                        " type=" + reference.getReferenceType() +
                        " function=" + (function == null
                            ? "none"
                            : function.getName() + " " +
                                function.getEntryPoint()));
                }
            }

            for (Function function : functions) {
                monitor.checkCancelled();
                println("DECOMPILE " + function.getName() + " " +
                    function.getEntryPoint());
                DecompileResults result =
                    decompiler.decompileFunction(function, 120, monitor);
                if (result.decompileCompleted() &&
                    result.getDecompiledFunction() != null) {
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
