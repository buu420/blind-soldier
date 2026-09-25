// Export every discovered native function without changing the program.
// Generated game code belongs in a private research directory, never this repository.
//@category BlindSoldier.Research

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.symbol.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.time.Instant;
import java.util.*;

public class ExportWholeGameEngine extends GhidraScript {
    private final Gson json = new GsonBuilder().disableHtmlEscaping().create();
    private Path root;

    private Map<String,Object> row(Object... entries) {
        Map<String,Object> map = new LinkedHashMap<>();
        for (int i=0; i<entries.length; i+=2) map.put((String)entries[i], entries[i+1]);
        return map;
    }

    private void writeJson(Path path, Object data) throws IOException {
        Files.writeString(path, json.toJson(data) + "\n", StandardCharsets.UTF_8);
    }

    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 1 || args.length > 2)
            throw new IllegalArgumentException("ExportWholeGameEngine <private-output-directory> [timeout-seconds]");
        root = Path.of(args[0]).toAbsolutePath().normalize();
        int timeout = args.length == 2 ? Integer.parseInt(args[1]) : 30;
        if (timeout < 1 || timeout > 600) throw new IllegalArgumentException("Timeout must be 1..600 seconds.");
        Files.createDirectories(root.resolve("functions"));
        String binaryHash = currentProgram.getExecutableSHA256();
        Path identityPath = root.resolve("identity.json");
        if (Files.exists(identityPath)) {
            Map<?,?> prior = json.fromJson(Files.readString(identityPath), Map.class);
            if (!Objects.equals(prior.get("sha256"), binaryHash))
                throw new IllegalStateException("Existing export belongs to a different executable.");
        }
        writeJson(identityPath, row("program",currentProgram.getName(),"sha256",binaryHash,
            "language",currentProgram.getLanguageID().toString(),
            "compiler",currentProgram.getCompilerSpec().getCompilerSpecID().toString(),
            "imageBase",currentProgram.getImageBase().toString(),
            "scope","All functions discovered by Ghidra analysis. Decompiled C is approximate, not original source."));

        AddressSet executable = new AddressSet();
        List<Object> blocks = new ArrayList<>();
        for (MemoryBlock block : currentProgram.getMemory().getBlocks()) {
            blocks.add(row("name",block.getName(),"start",block.getStart().toString(),
                "end",block.getEnd().toString(),"bytes",block.getSize(),
                "initialized",block.isInitialized(),"read",block.isRead(),
                "write",block.isWrite(),"execute",block.isExecute()));
            if (block.isExecute() && block.isInitialized()) executable.add(block.getStart(),block.getEnd());
        }
        writeJson(root.resolve("memory-blocks.json"),blocks);
        try (BufferedWriter output = Files.newBufferedWriter(root.resolve("symbols.jsonl"),StandardCharsets.UTF_8)) {
            SymbolIterator symbols = currentProgram.getSymbolTable().getAllSymbols(true);
            while (symbols.hasNext()) {
                monitor.checkCancelled();
                Symbol symbol = symbols.next();
                output.write(json.toJson(row("address",symbol.getAddress().toString(),
                    "name",symbol.getName(true),"type",symbol.getSymbolType().toString(),
                    "source",symbol.getSource().toString(),"external",symbol.isExternal())));
                output.newLine();
            }
        }

        FunctionManager functions = currentProgram.getFunctionManager();
        int total = 0, externalCount=0, processed=0, succeeded=0, failed=0, reused=0;
        for (FunctionIterator count = functions.getFunctions(true); count.hasNext(); ) { count.next(); total++; }
        try (BufferedWriter output = Files.newBufferedWriter(root.resolve("external-functions.jsonl"),StandardCharsets.UTF_8)) {
            FunctionIterator externals = functions.getExternalFunctions();
            while (externals.hasNext()) {
                Function function = externals.next();
                output.write(json.toJson(row("entry",function.getEntryPoint().toString(),
                    "name",function.getName(true),"prototype",function.getPrototypeString(true,true))));
                output.newLine(); externalCount++;
            }
        }
        AddressSet functionBytes = new AddressSet();
        DecompInterface decompiler = new DecompInterface();
        decompiler.toggleCCode(true);
        decompiler.toggleSyntaxTree(false);
        if (!decompiler.openProgram(currentProgram)) throw new IOException(decompiler.getLastMessage());
        String started = Instant.now().toString();
        try (BufferedWriter index = Files.newBufferedWriter(root.resolve("functions.jsonl"),StandardCharsets.UTF_8);
             BufferedWriter calls = Files.newBufferedWriter(root.resolve("calls.jsonl"),StandardCharsets.UTF_8)) {
            FunctionIterator iterator = functions.getFunctions(true);
            while (iterator.hasNext()) {
                monitor.checkCancelled();
                Function function = iterator.next();
                String entry = function.getEntryPoint().toString();
                Path directory = root.resolve("functions").resolve(entry.substring(0,Math.min(4,entry.length())));
                Files.createDirectories(directory);
                Path cPath = directory.resolve(entry+".c");
                Path metaPath = directory.resolve(entry+".json");
                Path asmPath = directory.resolve(entry+".asm");
                functionBytes.add(function.getBody());
                Map<String,Object> record = row("entry",entry,"name",function.getName(true),
                    "prototype",function.getPrototypeString(true,true),"bytes",function.getBody().getNumAddresses(),
                    "thunk",function.isThunk(),"external",function.isExternal());
                boolean success = false;
                // Re-export on every run: callee signatures and global data types can
                // change even when this function's bytes and prototype have not.
                if (!success) {
                    try {
                        DecompileResults result = decompiler.decompileFunction(function,timeout,monitor);
                        success = result.decompileCompleted() && result.getDecompiledFunction()!=null;
                        record.put("error",result.getErrorMessage());
                        if (success) {
                            String code = result.getDecompiledFunction().getC();
                            record.put("hasDecompilerWarnings",code.contains("WARNING:"));
                            record.put("hasTruncatedControlFlow",code.contains("halt_baddata") || code.contains("Bad instruction") || code.contains("Unable to resolve"));
                            Files.writeString(cPath,code,StandardCharsets.UTF_8);
                        }
                    } catch (Exception error) {
                        record.put("error",error.toString());
                    }
                    try (BufferedWriter assembly = Files.newBufferedWriter(asmPath,StandardCharsets.UTF_8)) {
                        InstructionIterator instructions = currentProgram.getListing().getInstructions(function.getBody(),true);
                        while (instructions.hasNext()) {
                            Instruction instruction = instructions.next();
                            assembly.write(instruction.getAddress()+"\t"+instruction.toString());
                            for (Reference reference : instruction.getReferencesFrom())
                                assembly.write("\t["+reference.getReferenceType()+" "+reference.getToAddress()+"]");
                            assembly.newLine();
                        }
                    }
                }
                record.put("decompileCompleted",success);
                record.put("cPath",success ? root.relativize(cPath).toString() : null);
                record.put("assemblyPath",root.relativize(asmPath).toString());
                writeJson(metaPath,record);
                index.write(json.toJson(record)); index.newLine();
                for (Function callee : function.getCalledFunctions(monitor)) {
                    calls.write(json.toJson(row("from",entry,"to",callee.getEntryPoint().toString(),
                        "name",callee.getName(true),"external",callee.isExternal())));
                    calls.newLine();
                }
                processed++;
                if (success) succeeded++; else failed++;
                if (processed % 100 == 0) {
                    index.flush(); calls.flush();
                    writeJson(root.resolve("progress.json"),row("started",started,"processed",processed,
                        "discoveredFunctionCount",total,"succeeded",succeeded,"failed",failed,"reused",reused));
                    println("ENGINE_EXPORT processed="+processed+" discovered="+total+" failed="+failed);
                }
            }
        } finally { decompiler.dispose(); }
        AddressSet uncovered = executable.subtract(functionBytes);
        List<Object> ranges = new ArrayList<>();
        for (AddressRange range : uncovered.getAddressRanges())
            ranges.add(row("start",range.getMinAddress().toString(),"end",range.getMaxAddress().toString(),"bytes",range.getLength()));
        writeJson(root.resolve("executable-ranges-outside-functions.json"),ranges);
        writeJson(root.resolve("summary.json"),row("started",started,"finished",Instant.now().toString(),
            "program",currentProgram.getName(),"sha256",binaryHash,"processed",processed,
            "discoveredFunctionCount",total,"externalFunctionCount",externalCount,
            "functionManagerTotal",functions.getFunctionCount(),"succeeded",succeeded,"failed",failed,"reused",reused,
            "executableBytes",executable.getNumAddresses(),
            "executableBytesOutsideFunctions",uncovered.getNumAddresses(),
            "note","Unassigned executable bytes can be data, padding, or undiscovered code. All discovered function exports were attempted; engine semantics and live behavior are not thereby verified."));
        println("ENGINE_EXPORT complete processed="+processed+" succeeded="+succeeded+" failed="+failed);
    }
}
