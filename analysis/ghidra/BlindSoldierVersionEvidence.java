// Machine-readable Ghidra evidence for Blind Soldier's guarded x86
// Version proxy and stock 7th Heaven AppLoader readiness gate.

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.AccessMode;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;

import ghidra.app.script.GhidraScript;
import ghidra.app.util.bin.FileByteProvider;
import ghidra.app.util.bin.format.pe.DataDirectory;
import ghidra.app.util.bin.format.pe.ExportDataDirectory;
import ghidra.app.util.bin.format.pe.ExportInfo;
import ghidra.app.util.bin.format.pe.FileHeader;
import ghidra.app.util.bin.format.pe.NTHeader;
import ghidra.app.util.bin.format.pe.OptionalHeader;
import ghidra.app.util.bin.format.pe.PortableExecutable;
import ghidra.app.util.bin.format.pe.PortableExecutable.SectionLayout;
import ghidra.program.model.listing.Data;
import ghidra.program.model.listing.DataIterator;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;
import ghidra.program.model.scalar.Scalar;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;

public class BlindSoldierVersionEvidence extends GhidraScript {
    private static final String[] FORBIDDEN = {
        "RegCreateKeyEx", "RegSetValue", "Image File Execution Options",
        "Debugger", "/install", "/uninstall",
        "VirtualAllocEx", "WriteProcessMemory", "CreateRemoteThread"
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
        if (!kind.equals("version-proxy")) {
            throw new Exception("Unexpected Version evidence kind: " + kind);
        }

        File executable = new File(currentProgram.getExecutablePath());
        byte[] bytes = Files.readAllBytes(executable.toPath());
        PeEvidence pe = readPe(executable);
        Set<String> evidence = collectEvidence(bytes);

        LinkedHashMap<String, Boolean> required = new LinkedHashMap<>();
        required.put("SystemVersionLoad",
            contains(evidence, "GetSystemDirectoryW") &&
            contains(evidence, "version.dll") &&
            contains(evidence, "LoadLibraryW") &&
            contains(evidence, "LoadLibraryExW"));
        required.put("HardenedVersionCache",
            contains(evidence, "version-system-x86-") &&
            contains(evidence, "NativeCache") &&
            contains(evidence, "MoveFileExW") &&
            contains(evidence, "BCryptCreateHash") &&
            contains(evidence, "BCryptHashData"));
        required.put("AppLoaderSignatureFiles",
            contains(evidence, "dinput.dll") &&
            contains(evidence, "AppProxy.runtimeconfig.json") &&
            contains(evidence, "AppProxy.dll") &&
            contains(evidence, "AppWrapper.dll") &&
            contains(evidence, "nethost.dll"));
        required.put("OrderedAppLoaderMarkers",
            contains(evidence, "AppLoader init log") &&
            contains(evidence, "AppLoader started successfully") &&
            contains(evidence, "waiting-for-success") &&
            contains(evidence, "ready-seventh-heaven"));
        required.put("AppLoaderTimeout120000", containsScalar(120000L));
        required.put("HostRootGuards",
            contains(evidence, "ff7_en.exe") &&
            contains(evidence, "ff7.exe") &&
            contains(evidence, "within four parent directories"));
        required.put("WorkerAndBroker",
            contains(evidence, "CreateThread") &&
            contains(evidence, "CreateProcessW") &&
            contains(evidence, "Blind-Soldier-Bootstrap-x86.exe") &&
            contains(evidence, "Local\\BlindSoldier.Ready."));
        required.put("NoWinmmForwardingSurface",
            !contains(evidence, "GetSystemWow64DirectoryW") &&
            !hasWinmmExport(pe.exports));
        required.put("NoEmbeddedExternalRuntime",
            !hasNestedPortableExecutable(bytes) && !hasZipArchive(bytes));

        List<String> forbidden = new ArrayList<>();
        for (String name : FORBIDDEN) {
            boolean found = name.equals("Debugger")
                ? containsExact(evidence, name) : contains(evidence, name);
            if (found) forbidden.add(name);
        }

        writeReport(report, kind, executable, pe.machine, required, forbidden,
            pe.exports);
        println("BLIND_SOLDIER_GHIDRA_EVIDENCE " + report);
        if (pe.machine != expectedMachine || required.containsValue(false) ||
                !forbidden.isEmpty() || pe.exports.size() != 17) {
            throw new Exception(
                "Version evidence did not satisfy the portable contract.");
        }
    }

    private Set<String> collectEvidence(byte[] bytes) throws Exception {
        Set<String> result = new LinkedHashSet<>();
        SymbolIterator symbols =
            currentProgram.getSymbolTable().getAllSymbols(true);
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
        addRawStrings(result, bytes, false);
        addRawStrings(result, bytes, true);
        return result;
    }

    private boolean containsScalar(long value) throws Exception {
        InstructionIterator instructions =
            currentProgram.getListing().getInstructions(true);
        while (instructions.hasNext()) {
            monitor.checkCancelled();
            Instruction instruction = instructions.next();
            for (int index = 0; index < instruction.getNumOperands(); ++index) {
                for (Object object : instruction.getOpObjects(index)) {
                    if (object instanceof Scalar &&
                            ((Scalar)object).getUnsignedValue() == value) {
                        return true;
                    }
                }
            }
        }
        return false;
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

    private static boolean hasWinmmExport(List<ExportInfo> exports) {
        for (ExportInfo item : exports) {
            String name = item.getName();
            if (name == null) continue;
            String normalized = name.toLowerCase(Locale.ROOT);
            if (normalized.startsWith("wave") ||
                    normalized.startsWith("midi") ||
                    normalized.startsWith("mixer") ||
                    normalized.startsWith("joy") ||
                    normalized.startsWith("timeget") ||
                    normalized.startsWith("playsound")) {
                return true;
            }
        }
        return false;
    }

    private static boolean hasNestedPortableExecutable(byte[] bytes) {
        for (int offset = 1; offset + 64 < bytes.length; ++offset) {
            if (bytes[offset] != 0x4d || bytes[offset + 1] != 0x5a) continue;
            int relative = readInt32(bytes, offset + 0x3c);
            long signature = (long)offset + relative;
            if (relative >= 0x40 && signature >= 0 &&
                    signature + 4 <= bytes.length &&
                    bytes[(int)signature] == 0x50 &&
                    bytes[(int)signature + 1] == 0x45 &&
                    bytes[(int)signature + 2] == 0 &&
                    bytes[(int)signature + 3] == 0) {
                return true;
            }
        }
        return false;
    }

    private static boolean hasZipArchive(byte[] bytes) {
        for (int index = 0; index + 4 <= bytes.length; ++index) {
            if (bytes[index] == 0x50 && bytes[index + 1] == 0x4b &&
                    bytes[index + 2] == 0x03 && bytes[index + 3] == 0x04) {
                return true;
            }
        }
        return false;
    }

    private static int readInt32(byte[] bytes, int offset) {
        if (offset < 0 || offset + 4 > bytes.length) return -1;
        return (bytes[offset] & 0xff) |
            ((bytes[offset + 1] & 0xff) << 8) |
            ((bytes[offset + 2] & 0xff) << 16) |
            ((bytes[offset + 3] & 0xff) << 24);
    }

    private static PeEvidence readPe(File executable) throws Exception {
        try (FileByteProvider provider =
                new FileByteProvider(executable, null, AccessMode.READ)) {
            PortableExecutable pe =
                new PortableExecutable(provider, SectionLayout.FILE, true, false);
            NTHeader nt = pe.getNTHeader();
            if (nt == null || nt.getOptionalHeader() == null) {
                throw new Exception("Ghidra could not parse the PE header.");
            }
            FileHeader header = nt.getFileHeader();
            int machine = Short.toUnsignedInt(header.getMachine());
            List<ExportInfo> exports = new ArrayList<>();
            DataDirectory[] directories =
                nt.getOptionalHeader().getDataDirectories();
            if (directories != null &&
                    directories.length >
                        OptionalHeader.IMAGE_DIRECTORY_ENTRY_EXPORT &&
                    directories[OptionalHeader.IMAGE_DIRECTORY_ENTRY_EXPORT]
                        instanceof ExportDataDirectory) {
                ExportDataDirectory directory = (ExportDataDirectory)
                    directories[OptionalHeader.IMAGE_DIRECTORY_ENTRY_EXPORT];
                exports.addAll(Arrays.asList(directory.getExports()));
            }
            exports.sort(Comparator.comparingInt(ExportInfo::getOrdinal));
            return new PeEvidence(machine, exports);
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

    private static boolean containsExact(Set<String> values, String needle) {
        for (String value : values) {
            if (value != null && value.equalsIgnoreCase(needle)) return true;
        }
        return false;
    }

    private static void writeReport(Path report, String kind, File executable,
            int machine, LinkedHashMap<String, Boolean> required,
            List<String> forbidden, List<ExportInfo> exports) throws Exception {
        StringBuilder json = new StringBuilder();
        json.append("{\n  \"schemaVersion\": 1,\n")
            .append("  \"marker\": \"BLIND_SOLDIER_GHIDRA_EVIDENCE\",\n")
            .append("  \"kind\": \"").append(escape(kind)).append("\",\n")
            .append("  \"program\": \"")
            .append(escape(executable.getAbsolutePath()))
            .append("\",\n  \"sha256\": \"")
            .append(sha256(executable.toPath()))
            .append("\",\n  \"machine\": ").append(machine).append(",\n")
            .append("  \"required\": {");
        int index = 0;
        for (Map.Entry<String, Boolean> entry : required.entrySet()) {
            if (index++ > 0) json.append(',');
            json.append("\n    \"").append(escape(entry.getKey()))
                .append("\": ").append(entry.getValue());
        }
        json.append("\n  },\n  \"forbidden\": [");
        for (index = 0; index < forbidden.size(); ++index) {
            if (index > 0) json.append(',');
            json.append("\"").append(escape(forbidden.get(index)))
                .append("\"");
        }
        json.append("],\n  \"exports\": [");
        for (index = 0; index < exports.size(); ++index) {
            if (index > 0) json.append(',');
            ExportInfo item = exports.get(index);
            String name = item.getName();
            boolean noname = name == null || name.isEmpty();
            json.append("\n    { \"ordinal\": ").append(item.getOrdinal())
                .append(", \"name\": ");
            if (noname) json.append("null");
            else json.append("\"").append(escape(name)).append("\"");
            json.append(", \"noname\": ").append(noname).append(" }");
        }
        json.append("\n  ],\n  \"tool\": \"Ghidra headless\"\n}\n");
        Files.createDirectories(report.getParent());
        Files.writeString(report, json.toString(), StandardCharsets.UTF_8);
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
        for (byte item : digest.digest()) {
            value.append(String.format("%02X", item & 0xff));
        }
        return value.toString();
    }

    private static String escape(String value) {
        return value.replace("\\", "\\\\").replace("\"", "\\\"")
            .replace("\r", "\\r").replace("\n", "\\n");
    }

    private static final class PeEvidence {
        final int machine;
        final List<ExportInfo> exports;

        PeEvidence(int machine, List<ExportInfo> exports) {
            this.machine = machine;
            this.exports = exports;
        }
    }
}
