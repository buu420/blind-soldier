// Ghidra headless post-analysis evidence for Blind Soldier's native installer
// and architecture-matched launchers.

import java.util.ArrayList;
import java.util.Arrays;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Data;
import ghidra.program.model.listing.DataIterator;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;

public class BlindSoldierNativeEvidence extends GhidraScript {
    @Override
    public void run() throws Exception {
        String programName = currentProgram.getName();
        boolean installer = programName.toLowerCase(Locale.ROOT).contains("installer");
        List<String> required = installer
            ? Arrays.asList(
                "RegCreateKeyExW",
                "RegQueryValueExW",
                "RegSetValueExW",
                "RegDeleteValueW",
                "ShellExecuteExW",
                "MessageBoxW",
                "Image File Execution Options",
                "Microsoft.WindowsDesktop.App",
                "/uninstall")
            : Arrays.asList(
                "CreateProcessW",
                "DebugActiveProcessStop",
                "VirtualAllocEx",
                "WriteProcessMemory",
                "CreateRemoteThread",
                "CreateToolhelp32Snapshot",
                "Module32FirstW",
                "GetModuleHandleExW",
                "CreateMutexW",
                "MoveFileExW",
                "LoadLibraryW",
                "ResumeThread");

        Set<String> symbolNames = new LinkedHashSet<>();
        SymbolIterator symbols = currentProgram.getSymbolTable().getAllSymbols(true);
        while (symbols.hasNext()) {
            monitor.checkCancelled();
            Symbol symbol = symbols.next();
            symbolNames.add(symbol.getName());
        }

        List<String> strings = new ArrayList<>();
        DataIterator dataIterator = currentProgram.getListing().getDefinedData(true);
        while (dataIterator.hasNext()) {
            monitor.checkCancelled();
            Data data = dataIterator.next();
            Object value = data.getValue();
            if (value instanceof String) {
                strings.add((String)value);
            }
        }

        println("BLIND_SOLDIER_NATIVE_EVIDENCE program=" + programName);
        println("  language=" + currentProgram.getLanguageID());
        println("  compiler=" + currentProgram.getCompilerSpec().getCompilerSpecID());
        List<String> missing = new ArrayList<>();
        for (String needle : required) {
            boolean found = containsIgnoreCase(symbolNames, needle) ||
                containsIgnoreCase(strings, needle);
            println("  " + needle + "=" + found);
            if (!found) {
                missing.add(needle);
            }
        }
        if (!missing.isEmpty()) {
            throw new Exception("Required native evidence missing: " +
                String.join(", ", missing));
        }
    }

    private static boolean containsIgnoreCase(Iterable<String> values,
                                               String needle) {
        String normalized = needle.toLowerCase(Locale.ROOT);
        for (String value : values) {
            if (value != null &&
                value.toLowerCase(Locale.ROOT).contains(normalized)) {
                return true;
            }
        }
        return false;
    }
}
