// Reports FFVII imports relevant to Reloaded-II delayed initialization.
//@category BlindSoldier

import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;

public class DumpDelayHookImports extends GhidraScript {
    @Override
    protected void run() throws Exception {
        println("Program: " + currentProgram.getExecutablePath());
        println("Image base: " + currentProgram.getImageBase());

        FunctionIterator functions = currentProgram.getFunctionManager().getExternalFunctions();
        int externalCount = 0;
        while (functions.hasNext()) {
            Function function = functions.next();
            externalCount++;
            String namespace = function.getParentNamespace().getName();
            String normalizedNamespace = namespace.toLowerCase();
            String normalizedName = function.getName().toLowerCase();
            if (normalizedNamespace.contains("d3d11") ||
                normalizedNamespace.contains("steam_api64") ||
                normalizedName.contains("d3d11") ||
                normalizedName.contains("steamapi_init") ||
                normalizedName.contains("verticalblank")) {

                int references = 0;
                ReferenceIterator iterator = currentProgram.getReferenceManager()
                    .getReferencesTo(function.getEntryPoint());
                while (iterator.hasNext()) {
                    Reference reference = iterator.next();
                    references++;
                    println("  reference from " + reference.getFromAddress());
                }

                println("Import " + namespace + "!" +
                    function.getName() + " at " + function.getEntryPoint() +
                    ", references=" + references);
            }
        }
        println("External function count: " + externalCount);

        SymbolIterator symbols = currentProgram.getSymbolTable().getAllSymbols(true);
        while (symbols.hasNext()) {
            Symbol symbol = symbols.next();
            String normalizedName = symbol.getName().toLowerCase();
            if (normalizedName.contains("d3d11") ||
                normalizedName.contains("steamapi_init") ||
                normalizedName.contains("verticalblank")) {
                int references = 0;
                ReferenceIterator iterator = currentProgram.getReferenceManager()
                    .getReferencesTo(symbol.getAddress());
                while (iterator.hasNext()) {
                    Reference reference = iterator.next();
                    references++;
                    println("  symbol reference from " + reference.getFromAddress());
                }
                println("Symbol " + symbol.getName(true) + " at " + symbol.getAddress() +
                    ", type=" + symbol.getSymbolType() + ", references=" + references);
            }
        }
    }
}
