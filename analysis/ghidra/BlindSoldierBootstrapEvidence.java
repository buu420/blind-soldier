// Machine-readable Ghidra evidence for Blind Soldier's architecture-matched
// portable launch/attach broker.

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.AccessMode;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;

import ghidra.app.script.GhidraScript;
import ghidra.app.util.bin.FileByteProvider;
import ghidra.app.util.bin.format.pe.FileHeader;
import ghidra.app.util.bin.format.pe.NTHeader;
import ghidra.app.util.bin.format.pe.PortableExecutable;
import ghidra.app.util.bin.format.pe.PortableExecutable.SectionLayout;
import ghidra.program.model.listing.Data;
import ghidra.program.model.listing.DataIterator;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;

public class BlindSoldierBootstrapEvidence extends GhidraScript {
    private static final String[] FORBIDDEN = {
        "RegCreateKeyEx", "RegSetValue", "Image File Execution Options",
        "Debugger", "/install", "/uninstall"
    };

    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 3) {
            throw new Exception("Expected report path, kind, and PE machine.");
        }
        Path report = Path.of(args[0]).toAbsolutePath();
        String kind = args[1];
        int expectedMachine = Integer.parseInt(args[2]);
        File executable = new File(currentProgram.getExecutablePath());
        int machine = peMachine(executable);
        Set<String> evidence = collectEvidence(executable.toPath());

        LinkedHashMap<String, Boolean> required = new LinkedHashMap<>();
        if (kind.equals("bootstrap-x86")) {
            require(required, evidence, "OpenProcess");
            require(required, evidence, "QueryFullProcessImageNameW");
            require(required, evidence, "SetEvent");
        }
        else if (kind.equals("bootstrap-x64")) {
            require(required, evidence, "CreateProcessW");
            require(required, evidence, "ResumeThread");
        }
        else {
            throw new Exception("Unexpected broker evidence kind: " + kind);
        }
        for (String name : new String[] {
                "VirtualAllocEx", "WriteProcessMemory", "CreateRemoteThread",
                "LoadLibraryW", "CreateMutexW", "MoveFileExW" }) {
            require(required, evidence, name);
        }
        required.put("PrivateRuntime",
            contains(evidence, "hostfxr.dll") && contains(evidence, "9.0.8") &&
            contains(evidence, "Runtime"));

        List<String> forbidden = new ArrayList<>();
        for (String name : FORBIDDEN) {
            boolean found = name.equals("Debugger")
                ? containsExact(evidence, name) : contains(evidence, name);
            if (found) forbidden.add(name);
        }
        writeReport(report, kind, executable, machine, required, forbidden);
        println("BLIND_SOLDIER_GHIDRA_EVIDENCE " + report);
        if (machine != expectedMachine || required.containsValue(false) ||
                !forbidden.isEmpty()) {
            throw new Exception("Broker evidence did not satisfy the portable contract.");
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
                result.add(symbol.getParentNamespace().getName() + "!" + symbol.getName());
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

    private static void require(Map<String, Boolean> required,
                                Set<String> evidence, String name) {
        required.put(name, contains(evidence, name));
    }

    private static boolean contains(Set<String> values, String needle) {
        String normalized = needle.toLowerCase(Locale.ROOT);
        for (String value : values) {
            if (value != null && value.toLowerCase(Locale.ROOT).contains(normalized)) {
                return true;
            }
        }
        return false;
    }

    private static boolean containsExact(Set<String> values, String needle) {
        for (String value : values) {
            if (value != null && value.equalsIgnoreCase(needle)) return true;
        }
        return false;
    }

    private static void writeReport(Path report, String kind, File executable,
            int machine, LinkedHashMap<String, Boolean> required,
            List<String> forbidden) throws Exception {
        StringBuilder json = new StringBuilder();
        json.append("{\n  \"schemaVersion\": 1,\n")
            .append("  \"marker\": \"BLIND_SOLDIER_GHIDRA_EVIDENCE\",\n")
            .append("  \"kind\": \"").append(escape(kind)).append("\",\n")
            .append("  \"program\": \"").append(escape(executable.getAbsolutePath()))
            .append("\",\n  \"sha256\": \"").append(sha256(executable.toPath()))
            .append("\",\n  \"machine\": ").append(machine).append(",\n")
            .append("  \"required\": {");
        int index = 0;
        for (Map.Entry<String, Boolean> entry : required.entrySet()) {
            if (index++ > 0) json.append(',');
            json.append("\n    \"").append(escape(entry.getKey())).append("\": ")
                .append(entry.getValue());
        }
        json.append("\n  },\n  \"forbidden\": [");
        for (index = 0; index < forbidden.size(); ++index) {
            if (index > 0) json.append(',');
            json.append("\"").append(escape(forbidden.get(index))).append("\"");
        }
        json.append("],\n  \"exports\": [],\n")
            .append("  \"tool\": \"Ghidra headless\"\n}\n");
        Files.createDirectories(report.getParent());
        Files.writeString(report, json.toString(), StandardCharsets.UTF_8);
    }

    private static int peMachine(File executable) throws Exception {
        try (FileByteProvider provider =
                new FileByteProvider(executable, null, AccessMode.READ)) {
            PortableExecutable pe =
                new PortableExecutable(provider, SectionLayout.FILE, true, false);
            NTHeader nt = pe.getNTHeader();
            if (nt == null) throw new Exception("Ghidra could not parse the PE header.");
            FileHeader header = nt.getFileHeader();
            return Short.toUnsignedInt(header.getMachine());
        }
    }

    private static String sha256(Path path) throws Exception {
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        byte[] buffer = new byte[1024 * 1024];
        try (java.io.InputStream input = Files.newInputStream(path)) {
            int count;
            while ((count = input.read(buffer)) >= 0) {
                if (count > 0) digest.update(buffer, 0, count);
            }
        }
        StringBuilder value = new StringBuilder(64);
        for (byte item : digest.digest()) value.append(String.format("%02X", item & 0xff));
        return value.toString();
    }

    private static String escape(String value) {
        return value.replace("\\", "\\\\").replace("\"", "\\\"")
            .replace("\r", "\\r").replace("\n", "\\n");
    }
}
