// Reports registry evidence from the lifecycle executable shipped through
// Amethyst's Accessibility Mod Manager.
//@category BlindSoldier

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.Locale;
import java.util.Map;
import java.util.Set;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Data;
import ghidra.program.model.listing.DataIterator;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;

public class DumpBlindSoldierInstallerRegistryEvidence extends GhidraScript {
    private static final String[] REQUIRED = {
        "RegCreateKeyExW", "RegSetValueExW", "RegDeleteValueW",
        "Image File Execution Options", "BlindSoldier_Launcher.exe"
    };

    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 2) {
            throw new Exception("Expected report path and target executable.");
        }
        Path report = Path.of(args[0]).toAbsolutePath();
        String programPath = currentProgram.getExecutablePath();
        if (programPath.matches("^/[A-Za-z]:/.*")) {
            programPath = programPath.substring(1);
        }
        Path executable = Path.of(programPath);
        Set<String> evidence = collectEvidence(executable);
        Map<String, Boolean> results = new LinkedHashMap<>();
        for (String required : REQUIRED) {
            boolean found = contains(evidence, required);
            results.put(required, found);
            println((found ? "FOUND " : "MISSING ") + required);
        }
        boolean targetFound = contains(evidence, args[1]);
        results.put(args[1], targetFound);
        println((targetFound ? "FOUND " : "MISSING ") + args[1]);

        StringBuilder json = new StringBuilder();
        json.append("{\n  \"schemaVersion\": 1,\n")
            .append("  \"tool\": \"Ghidra headless\",\n")
            .append("  \"program\": \"")
            .append(escape(executable.toAbsolutePath().toString()))
            .append("\",\n  \"required\": {\n");
        int index = 0;
        for (Map.Entry<String, Boolean> result : results.entrySet()) {
            json.append("    \"").append(escape(result.getKey()))
                .append("\": ").append(result.getValue());
            if (++index != results.size()) json.append(',');
            json.append('\n');
        }
        json.append("  }\n}\n");
        Files.createDirectories(report.getParent());
        Files.writeString(report, json.toString());
        if (results.containsValue(false)) {
            throw new Exception("Installer registry evidence is incomplete.");
        }
    }

    private Set<String> collectEvidence(Path executable) throws Exception {
        Set<String> result = new LinkedHashSet<>();
        SymbolIterator symbols = currentProgram.getSymbolTable().getAllSymbols(true);
        while (symbols.hasNext()) {
            monitor.checkCancelled();
            Symbol symbol = symbols.next();
            result.add(symbol.getName());
            if (symbol.getParentNamespace() != null) {
                result.add(symbol.getParentNamespace().getName() + "!" +
                    symbol.getName());
            }
        }
        DataIterator data = currentProgram.getListing().getDefinedData(true);
        while (data.hasNext()) {
            monitor.checkCancelled();
            Data item = data.next();
            Object value = item.getValue();
            if (value instanceof String) result.add((String)value);
        }
        byte[] bytes = Files.readAllBytes(executable);
        addRawStrings(result, bytes, false);
        addRawStrings(result, bytes, true);
        return result;
    }

    private static void addRawStrings(Set<String> values, byte[] bytes,
                                      boolean wide) {
        int phases = wide ? 2 : 1;
        int step = wide ? 2 : 1;
        for (int phase = 0; phase < phases; ++phase) {
            StringBuilder current = new StringBuilder();
            for (int offset = phase; offset + step - 1 < bytes.length;
                    offset += step) {
                int value = bytes[offset] & 0xff;
                boolean valid = value >= 0x20 && value <= 0x7e &&
                    (!wide || bytes[offset + 1] == 0);
                if (valid) current.append((char)value);
                else {
                    if (current.length() >= 4) values.add(current.toString());
                    current.setLength(0);
                }
            }
            if (current.length() >= 4) values.add(current.toString());
        }
    }

    private static boolean contains(Set<String> values, String needle) {
        String normalized = needle.toLowerCase(Locale.ROOT);
        for (String value : values) {
            if (value != null &&
                    value.toLowerCase(Locale.ROOT).contains(normalized)) {
                return true;
            }
        }
        return false;
    }

    private static String escape(String value) {
        return value.replace("\\", "\\\\").replace("\"", "\\\"")
            .replace("\r", "\\r").replace("\n", "\\n");
    }
}
