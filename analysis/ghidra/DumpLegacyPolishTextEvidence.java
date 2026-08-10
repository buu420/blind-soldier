// Ghidra headless evidence for the legacy single-byte field text renderer used
// by the public Polish Steam translation.

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class DumpLegacyPolishTextEvidence extends GhidraScript {
    private static final long FIELD_TEXT_RENDERER = 0x006E706DL;
    private static final long MENU_FONT_A_OBJECT = 0x00DC100CL;
    private static final long MENU_FONT_B_OBJECT = 0x00DC1010L;

    @Override
    public void run() throws Exception {
        Function renderer = getFunctionAt(toAddr(FIELD_TEXT_RENDERER));
        println("LEGACY_POLISH_TEXT_EVIDENCE program=" + currentProgram.getName());
        println("  language=" + currentProgram.getLanguageID());
        println("  renderer=" + (renderer == null
            ? "none"
            : renderer.getName() + " " + renderer.getEntryPoint()));
        if (renderer == null) {
            return;
        }

        println("  referencesFontA=" + referencesGlobal(renderer, toAddr(MENU_FONT_A_OBJECT)));
        println("  referencesFontB=" + referencesGlobal(renderer, toAddr(MENU_FONT_B_OBJECT)));

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            DecompileResults result = decompiler.decompileFunction(renderer, 120, monitor);
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

    private boolean referencesGlobal(Function function, Address global) {
        ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(global);
        while (references.hasNext()) {
            Reference reference = references.next();
            if (function.getBody().contains(reference.getFromAddress())) {
                println("  globalReference=" + global + " from=" + reference.getFromAddress());
                return true;
            }
        }
        return false;
    }
}
