// Machine-readable Ghidra evidence for Blind Soldier's guarded x86
// Version proxy and stock 7th Heaven AppLoader readiness gate.

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.AccessMode;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Comparator;
import java.util.Deque;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.function.Consumer;

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
import ghidra.program.model.block.CodeBlock;
import ghidra.program.model.block.PartitionCodeSubModel;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressOutOfBoundsException;
import ghidra.program.model.address.AddressSpace;
import ghidra.program.model.listing.Data;
import ghidra.program.model.listing.DataIterator;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.FunctionManager;
import ghidra.program.model.listing.Listing;
import ghidra.program.model.scalar.Scalar;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;
import ghidra.program.model.symbol.ReferenceManager;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;
import ghidra.program.model.symbol.SymbolTable;

public class BlindSoldierVersionEvidence extends GhidraScript {
    private static final String[] BEHAVIORAL_FORBIDDEN = {
        "Image File Execution Options", "Debugger", "/install", "/uninstall"
    };
    private static final String[] WORKER_WIDE_EVIDENCE = {
        "Local\\BlindSoldier.Ready.", "CreateProcessW(x86 accessibility broker)",
        "x86 broker started: "
    };
    private static final int BACK_REFERENCE_DEPTH = 2;

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
        BlindSoldierVersionEvidenceRules.ProgramFacts facts =
            collectFacts(bytes, pe.exports);
        LinkedHashMap<String, Boolean> required =
            BlindSoldierVersionEvidenceRules.evaluate(facts);
        List<String> forbidden = new ArrayList<>(
            BlindSoldierVersionEvidenceRules.forbiddenImports(facts));
        Set<String> raw = new LinkedHashSet<>();
        addRawStrings(raw, bytes, false);
        addRawStrings(raw, bytes, true);
        for (String marker : BEHAVIORAL_FORBIDDEN) {
            boolean found = marker.equals("Debugger")
                ? containsRawExact(raw, marker) : containsRaw(raw, marker);
            if (found && !forbidden.contains(marker)) forbidden.add(marker);
        }
        forbidden.sort(String.CASE_INSENSITIVE_ORDER);

        writeReport(report, kind, executable, pe.machine, required, forbidden,
            pe.exports);
        println("BLIND_SOLDIER_GHIDRA_EVIDENCE " + report);
        if (pe.machine != expectedMachine || required.containsValue(false) ||
                !forbidden.isEmpty() || pe.exports.size() != 17) {
            throw new Exception(
                "Version evidence did not satisfy the portable contract.");
        }
    }

    private BlindSoldierVersionEvidenceRules.ProgramFacts collectFacts(
            byte[] bytes, List<ExportInfo> exports) throws Exception {
        BlindSoldierVersionEvidenceRules.ProgramFacts facts =
            new BlindSoldierVersionEvidenceRules.ProgramFacts()
                .nestedPortableExecutable(hasNestedPortableExecutable(bytes))
                .zipArchive(hasZipArchive(bytes));
        for (ExportInfo item : exports) {
            if (item.getName() != null) facts.exportName(item.getName());
        }

        FunctionManager functionManager = currentProgram.getFunctionManager();
        Listing listing = currentProgram.getListing();
        ReferenceManager references = currentProgram.getReferenceManager();
        SymbolTable symbols = currentProgram.getSymbolTable();
        Map<Address, Function> functions = new LinkedHashMap<>();
        Map<Address, BlindSoldierVersionEvidenceRules.FunctionFacts> byEntry =
            new LinkedHashMap<>();
        FunctionIterator functionIterator = functionManager.getFunctions(true);
        while (functionIterator.hasNext()) {
            monitor.checkCancelled();
            Function function = functionIterator.next();
            if (function.isExternal()) continue;
            BlindSoldierVersionEvidenceRules.FunctionFacts functionFacts =
                new BlindSoldierVersionEvidenceRules.FunctionFacts(
                    functionId(function));
            functions.put(function.getEntryPoint(), function);
            byEntry.put(function.getEntryPoint(), functionFacts);
            facts.function(functionFacts);
        }

        Set<String> importNames = new LinkedHashSet<>();
        SymbolIterator externalSymbols = symbols.getExternalSymbols();
        while (externalSymbols.hasNext()) {
            monitor.checkCancelled();
            Symbol symbol = externalSymbols.next();
            String name = BlindSoldierVersionEvidenceRules.normalizeSymbol(
                symbol.getName());
            facts.importName(name);
            importNames.add(name);
            walkBackReferences(symbol.getAddress(), functionManager, byEntry,
                references, value -> value.symbol(name));
        }
        FunctionIterator externalFunctions = functionManager.getExternalFunctions();
        while (externalFunctions.hasNext()) {
            monitor.checkCancelled();
            String name = BlindSoldierVersionEvidenceRules.normalizeSymbol(
                externalFunctions.next().getName());
            facts.importName(name);
            importNames.add(name);
        }
        attachImportedSymbolBackReferences(importNames, functionManager,
            byEntry, references, symbols);
        attachTargetedUtf16StringBackReferences(functionManager, byEntry,
            references);

        for (Map.Entry<Address, Function> item : functions.entrySet()) {
            monitor.checkCancelled();
            Function function = item.getValue();
            String targetId = functionId(function);
            walkBackReferences(item.getKey(), functionManager, byEntry,
                references, value -> value.calls(targetId));
            BlindSoldierVersionEvidenceRules.FunctionFacts functionFacts =
                byEntry.get(item.getKey());
            for (Function called : function.getCalledFunctions(monitor)) {
                Function thunk = called.getThunkedFunction(true);
                if (called.isExternal()) {
                    functionFacts.symbol(called.getName());
                }
                else {
                    functionFacts.calls(functionId(called));
                }
                if (thunk != null && thunk.isExternal()) {
                    functionFacts.symbol(thunk.getName());
                }
            }
        }

        Memory instructionMemory = currentProgram.getMemory();
        AddressSpace defaultSpace = currentProgram.getAddressFactory()
            .getDefaultAddressSpace();
        PartitionCodeSubModel subroutineModel =
            new PartitionCodeSubModel(currentProgram, false);
        Map<Address, BlindSoldierVersionEvidenceRules.FunctionFacts>
            syntheticByStart = new LinkedHashMap<>();
        InstructionIterator instructions = listing.getInstructions(true);
        while (instructions.hasNext()) {
            monitor.checkCancelled();
            Instruction instruction = instructions.next();
            Function owner = functionManager.getFunctionContaining(
                instruction.getAddress());
            BlindSoldierVersionEvidenceRules.FunctionFacts functionFacts = null;
            if (owner != null && !owner.isExternal()) {
                functionFacts = byEntry.get(owner.getEntryPoint());
            }
            else {
                functionFacts = resolveSyntheticFunctionFacts(
                    instruction.getAddress(), subroutineModel,
                    syntheticByStart, facts);
            }
            if (functionFacts == null) continue;
            Set<Address> targetAddresses = new LinkedHashSet<>();
            for (int index = 0; index < instruction.getNumOperands(); ++index) {
                for (Object object : instruction.getOpObjects(index)) {
                    if (object instanceof Scalar) {
                        Scalar scalar = (Scalar)object;
                        long value = scalar.getUnsignedValue();
                        functionFacts.scalar(value);
                        try {
                            Address candidate = defaultSpace.getAddress(value);
                            MemoryBlock block = instructionMemory.getBlock(candidate);
                            if (block != null && block.isInitialized()) {
                                targetAddresses.add(candidate);
                            }
                        }
                        catch (AddressOutOfBoundsException ignored) {
                        }
                    }
                    else if (object instanceof Address) {
                        targetAddresses.add((Address)object);
                    }
                }
            }
            for (Reference reference : references.getReferencesFrom(
                    instruction.getAddress())) {
                targetAddresses.add(reference.getToAddress());
            }
            for (Address targetAddress : targetAddresses) {
                Function target = functionManager.getFunctionAt(targetAddress);
                if (target == null) {
                    target = functionManager.getFunctionContaining(targetAddress);
                }
                if (target != null) {
                    Function thunk = target.getThunkedFunction(true);
                    if (target.isExternal()) {
                        functionFacts.symbol(target.getName());
                    }
                    else {
                        functionFacts.calls(functionId(target));
                    }
                    if (thunk != null && thunk.isExternal()) {
                        functionFacts.symbol(thunk.getName());
                    }
                }
                else if (instruction.getFlowType().isCall()) {
                    BlindSoldierVersionEvidenceRules.FunctionFacts syntheticTarget =
                        resolveSyntheticFunctionFacts(targetAddress,
                            subroutineModel, syntheticByStart, facts);
                    if (syntheticTarget != null) {
                        functionFacts.calls(syntheticTarget.id);
                    }
                }
                for (Symbol symbol : symbols.getSymbols(targetAddress)) {
                    String name = BlindSoldierVersionEvidenceRules.normalizeSymbol(
                        symbol.getName());
                    if (containsIgnoreCase(importNames, name)) {
                        functionFacts.symbol(name);
                    }
                }
                if (instruction.getFlowType().isCall()) {
                    attachImportedCallTarget(targetAddress, importNames,
                        functionManager, listing, references, symbols,
                        functionFacts);
                }
                Data data = listing.getDefinedDataContaining(targetAddress);
                if (data != null && data.getValue() instanceof String) {
                    functionFacts.stringValue((String)data.getValue());
                }
                else {
                    String decoded = readBoundedUtf16Le(targetAddress);
                    if (decoded != null) functionFacts.stringValue(decoded);
                }
            }
        }

        DataIterator dataIterator = listing.getDefinedData(true);
        while (dataIterator.hasNext()) {
            monitor.checkCancelled();
            Data data = dataIterator.next();
            Object value = data.getValue();
            if (value instanceof String) {
                String stringValue = (String)value;
                walkBackReferences(data.getAddress(), functionManager, byEntry,
                    references, item -> item.stringValue(stringValue));
            }
        }
        return facts;
    }

    private BlindSoldierVersionEvidenceRules.FunctionFacts
            resolveSyntheticFunctionFacts(Address address,
            PartitionCodeSubModel subroutineModel,
            Map<Address, BlindSoldierVersionEvidenceRules.FunctionFacts>
                syntheticByStart,
            BlindSoldierVersionEvidenceRules.ProgramFacts facts)
            throws Exception {
        if (address == null || !address.isMemoryAddress()) return null;
        CodeBlock block = subroutineModel.getFirstCodeBlockContaining(
            address, monitor);
        if (block == null) return null;
        Address start = block.getFirstStartAddress();
        if (start == null) return null;
        BlindSoldierVersionEvidenceRules.FunctionFacts functionFacts =
            syntheticByStart.get(start);
        if (functionFacts == null) {
            functionFacts = new BlindSoldierVersionEvidenceRules.FunctionFacts(
                "subroutine:" + start);
            syntheticByStart.put(start, functionFacts);
            facts.function(functionFacts);
        }
        return functionFacts;
    }

    private void attachImportedSymbolBackReferences(Set<String> importNames,
            FunctionManager functionManager,
            Map<Address, BlindSoldierVersionEvidenceRules.FunctionFacts> byEntry,
            ReferenceManager references, SymbolTable symbols) throws Exception {
        SymbolIterator all = symbols.getAllSymbols(true);
        while (all.hasNext()) {
            monitor.checkCancelled();
            Symbol symbol = all.next();
            String imported = BlindSoldierVersionEvidenceRules.matchImportedSymbol(
                symbol.getName(), importNames);
            if (imported != null) {
                walkBackReferences(symbol.getAddress(), functionManager, byEntry,
                    references, value -> value.symbol(imported));
            }
        }
    }

    private void attachTargetedUtf16StringBackReferences(
            FunctionManager functionManager,
            Map<Address, BlindSoldierVersionEvidenceRules.FunctionFacts> byEntry,
            ReferenceManager references) throws Exception {
        Memory memory = currentProgram.getMemory();
        for (String value : WORKER_WIDE_EVIDENCE) {
            byte[] pattern = (value + "\0").getBytes(StandardCharsets.UTF_16LE);
            for (MemoryBlock block : memory.getBlocks()) {
                if (!block.isInitialized()) continue;
                Address cursor = block.getStart();
                while (cursor != null && block.contains(cursor)) {
                    monitor.checkCancelled();
                    Address found = memory.findBytes(cursor, block.getEnd(),
                        pattern, null, true, monitor);
                    if (found == null) break;
                    walkBackReferences(found, functionManager, byEntry,
                        references, item -> item.stringValue(value));
                    Address next = found.next();
                    if (next == null || !block.contains(next)) break;
                    cursor = next;
                }
            }
        }
    }

    private String readBoundedUtf16Le(Address start) {
        if (start == null || !start.isMemoryAddress()) return null;
        Memory memory = currentProgram.getMemory();
        if (!memory.contains(start)) return null;
        StringBuilder value = new StringBuilder();
        try {
            for (int index = 0; index < 512; ++index) {
                Address lowAddress = start.add((long)index * 2L);
                Address highAddress = lowAddress.add(1L);
                if (!memory.contains(lowAddress) || !memory.contains(highAddress)) {
                    return null;
                }
                int low = Byte.toUnsignedInt(memory.getByte(lowAddress));
                int high = Byte.toUnsignedInt(memory.getByte(highAddress));
                char character = (char)((high << 8) | low);
                if (character == 0) {
                    return value.length() >= 4 ? value.toString() : null;
                }
                if (Character.isISOControl(character) ||
                        Character.isSurrogate(character) ||
                        character == '\uffff') {
                    return null;
                }
                value.append(character);
            }
        }
        catch (Exception ignored) {
            return null;
        }
        return null;
    }

    private void attachImportedCallTarget(Address target,
            Set<String> importNames, FunctionManager functionManager,
            Listing listing, ReferenceManager references, SymbolTable symbols,
            BlindSoldierVersionEvidenceRules.FunctionFacts functionFacts) {
        attachImportedAddress(target, importNames, functionManager, symbols,
            functionFacts);
        Data pointer = listing.getDefinedDataAt(target);
        if (pointer != null) {
            Object value = pointer.getValue();
            if (value instanceof Address) {
                attachImportedAddress((Address)value, importNames,
                    functionManager, symbols, functionFacts);
            }
            for (Reference reference : pointer.getValueReferences()) {
                attachImportedReference(reference, importNames,
                    functionManager, symbols, functionFacts);
            }
        }
        for (Reference reference : references.getReferencesFrom(target)) {
            attachImportedReference(reference, importNames, functionManager,
                symbols, functionFacts);
        }
    }

    private void attachImportedReference(Reference reference,
            Set<String> importNames, FunctionManager functionManager,
            SymbolTable symbols,
            BlindSoldierVersionEvidenceRules.FunctionFacts functionFacts) {
        Symbol associated = symbols.getSymbol(reference);
        if (associated != null) {
            attachImportCandidate(associated.getName(), importNames,
                functionFacts);
        }
        attachImportedAddress(reference.getToAddress(), importNames,
            functionManager, symbols, functionFacts);
    }

    private void attachImportedAddress(Address address,
            Set<String> importNames, FunctionManager functionManager,
            SymbolTable symbols,
            BlindSoldierVersionEvidenceRules.FunctionFacts functionFacts) {
        if (address == null) return;
        for (Symbol symbol : symbols.getSymbols(address)) {
            attachImportCandidate(symbol.getName(), importNames, functionFacts);
        }
        Function target = functionManager.getFunctionAt(address);
        if (target == null) target = functionManager.getFunctionContaining(address);
        if (target == null) return;
        attachImportCandidate(target.getName(), importNames, functionFacts);
        Function thunk = target.getThunkedFunction(true);
        if (thunk != null) {
            attachImportCandidate(thunk.getName(), importNames, functionFacts);
        }
    }

    private static void attachImportCandidate(String candidate,
            Set<String> importNames,
            BlindSoldierVersionEvidenceRules.FunctionFacts functionFacts) {
        String imported = BlindSoldierVersionEvidenceRules.matchImportedSymbol(
            candidate, importNames);
        if (imported != null) functionFacts.symbol(imported);
    }

    private static String functionId(Function function) {
        return function.getEntryPoint().toString();
    }

    private void walkBackReferences(Address target,
            FunctionManager functionManager,
            Map<Address, BlindSoldierVersionEvidenceRules.FunctionFacts> byEntry,
            ReferenceManager references,
            Consumer<BlindSoldierVersionEvidenceRules.FunctionFacts> sink)
            throws Exception {
        Deque<AddressDepth> pending = new ArrayDeque<>();
        Set<Address> visited = new HashSet<>();
        pending.addLast(new AddressDepth(target, 0));
        while (!pending.isEmpty()) {
            monitor.checkCancelled();
            AddressDepth current = pending.removeFirst();
            if (!visited.add(current.address)) continue;
            ReferenceIterator iterator = references.getReferencesTo(
                current.address);
            while (iterator.hasNext()) {
                monitor.checkCancelled();
                Reference reference = iterator.next();
                Address from = reference.getFromAddress();
                Function owner = functionManager.getFunctionContaining(from);
                if (owner != null && !owner.isExternal()) {
                    BlindSoldierVersionEvidenceRules.FunctionFacts functionFacts =
                        byEntry.get(owner.getEntryPoint());
                    if (functionFacts != null) sink.accept(functionFacts);
                }
                else if (current.depth < BACK_REFERENCE_DEPTH &&
                        from.isMemoryAddress()) {
                    pending.addLast(new AddressDepth(from, current.depth + 1));
                }
            }
        }
    }

    private static boolean containsIgnoreCase(Set<String> values, String value) {
        for (String item : values) {
            if (item.equalsIgnoreCase(value)) return true;
        }
        return false;
    }

    private static final class AddressDepth {
        final Address address;
        final int depth;

        AddressDepth(Address address, int depth) {
            this.address = address;
            this.depth = depth;
        }
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

    private static boolean containsRaw(Set<String> values, String needle) {
        String normalized = needle.toLowerCase(Locale.ROOT);
        for (String value : values) {
            if (value != null &&
                    value.toLowerCase(Locale.ROOT).contains(normalized)) {
                return true;
            }
        }
        return false;
    }

    private static boolean containsRawExact(Set<String> values, String needle) {
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
