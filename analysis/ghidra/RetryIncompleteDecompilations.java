// Retry only hard failures in a completed private export, preserving all diagnostics.
//@category BlindSoldier.Research
import com.google.gson.*;
import ghidra.app.decompiler.*;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.time.Instant;
import java.util.*;

public class RetryIncompleteDecompilations extends GhidraScript {
    private final Gson json = new GsonBuilder().disableHtmlEscaping().create();

    @Override public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 1) throw new IllegalArgumentException("RetryIncompleteDecompilations <private-export-root> [entry ...]");
        Path root = Path.of(args[0]);
        Set<String> selected = new HashSet<>();
        for (int i=1; i<args.length; i++) selected.add(args[i].toLowerCase(Locale.ROOT));
        JsonObject identity = JsonParser.parseString(Files.readString(root.resolve("identity.json"))).getAsJsonObject();
        if (!identity.get("sha256").getAsString().equalsIgnoreCase(currentProgram.getExecutableSHA256()))
            throw new IllegalStateException("Export and Ghidra program identities differ");
        List<JsonObject> records = new ArrayList<>();
        for (String line : Files.readAllLines(root.resolve("functions.jsonl")))
            if (!line.isBlank()) records.add(JsonParser.parseString(line).getAsJsonObject());
        JsonObject summary = JsonParser.parseString(Files.readString(root.resolve("summary.json"))).getAsJsonObject();
        if (records.size() != summary.get("processed").getAsInt()) throw new IllegalStateException("Export has not finished");
        for (String entry : selected) {
            boolean exists = records.stream().anyMatch(record -> record.get("entry").getAsString().equalsIgnoreCase(entry));
            if (!exists) throw new IllegalArgumentException("Selected entry is absent from the export: "+entry);
        }
        List<JsonObject> retries = new ArrayList<>();
        Path retryPath = root.resolve("decompile-retries.json");
        if (Files.exists(retryPath)) {
            for (JsonElement previous : JsonParser.parseString(Files.readString(retryPath)).getAsJsonArray())
                retries.add(previous.getAsJsonObject());
        }
        int attempted = 0;
        for (JsonObject record : records) {
            monitor.checkCancelled();
            if (record.get("decompileCompleted").getAsBoolean()) continue;
            String entry = record.get("entry").getAsString();
            if (!selected.isEmpty() && !selected.contains(entry.toLowerCase(Locale.ROOT))) continue;
            attempted++;
            Function function = getFunctionAt(toAddr(entry));
            if (function == null) throw new IllegalStateException("Function missing from current analysis: "+entry);
            JsonObject history = new JsonObject();
            history.addProperty("entry",entry);
            history.add("previousError",record.get("error"));
            JsonArray attempts = new JsonArray();
            for (boolean eliminateUnreachable : new boolean[]{true,false}) {
                DecompileOptions options = new DecompileOptions();
                options.setMaxInstructions(500000);
                options.setMaxPayloadMBytes(128);
                options.setEliminateUnreachable(eliminateUnreachable);
                DecompInterface decompiler = new DecompInterface();
                decompiler.setOptions(options); decompiler.toggleCCode(true); decompiler.toggleSyntaxTree(false);
                try {
                    if (!decompiler.openProgram(currentProgram)) throw new IllegalStateException(decompiler.getLastMessage());
                    DecompileResults result = decompiler.decompileFunction(function,180,monitor);
                    boolean success = result.decompileCompleted() && result.getDecompiledFunction()!=null;
                    JsonObject attempt = new JsonObject();
                    attempt.addProperty("maxInstructions",500000); attempt.addProperty("timeoutSeconds",180);
                    attempt.addProperty("eliminateUnreachable",eliminateUnreachable);
                    attempt.addProperty("success",success); attempt.addProperty("error",result.getErrorMessage());
                    attempts.add(attempt);
                    record.addProperty("error",result.getErrorMessage());
                    if (success) {
                        String code = result.getDecompiledFunction().getC();
                        Path cPath = root.resolve("functions").resolve(entry.substring(0,4)).resolve(entry+".c");
                        Files.writeString(cPath,code,StandardCharsets.UTF_8);
                        record.addProperty("cPath",root.relativize(cPath).toString());
                        record.addProperty("decompileCompleted",true);
                        record.addProperty("hasDecompilerWarnings",code.contains("WARNING:"));
                        record.addProperty("hasTruncatedControlFlow",code.contains("halt_baddata") || code.contains("Bad instruction") || code.contains("Unable to resolve"));
                        record.addProperty("extendedLimitRetry",true);
                        break;
                    }
                } finally { decompiler.dispose(); }
            }
            history.add("attempts",attempts); retries.add(history);
            Path metadata = root.resolve("functions").resolve(entry.substring(0,4)).resolve(entry+".json");
            Files.writeString(metadata,json.toJson(record)+"\n",StandardCharsets.UTF_8);
            Files.writeString(retryPath,json.toJson(retries)+"\n",StandardCharsets.UTF_8);
            println("DECOMPILE_RETRY "+entry+" success="+record.get("decompileCompleted"));
        }
        StringBuilder index = new StringBuilder(); int succeeded=0;
        for (JsonObject record : records) {
            index.append(json.toJson(record)).append('\n');
            if (record.get("decompileCompleted").getAsBoolean()) succeeded++;
        }
        Files.writeString(root.resolve("functions.jsonl"),index.toString(),StandardCharsets.UTF_8);
        summary.addProperty("succeeded",succeeded); summary.addProperty("failed",records.size()-succeeded);
        summary.addProperty("retryFinished",Instant.now().toString());
        Files.writeString(root.resolve("summary.json"),json.toJson(summary)+"\n",StandardCharsets.UTF_8);
        println("DECOMPILE_RETRIES complete attempted="+attempted+" remaining="+(records.size()-succeeded));
    }
}
