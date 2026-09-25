// Recover function entry points from the game's native translated-function map.
// Run only against a private copy of the exact licensed legacy/loaded x64 image.
//@category BlindSoldier.Research
import com.google.gson.*;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.mem.MemoryBlock;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

public class SeedTranslatedEngineFunctions extends GhidraScript {
    @Override public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 3) throw new IllegalArgumentException("SeedTranslatedEngineFunctions <map-json> <legacy|host> <report-json>");
        JsonObject document = JsonParser.parseString(Files.readString(Path.of(args[0]))).getAsJsonObject();
        boolean legacy = args[1].equals("legacy");
        if (!legacy && !args[1].equals("host")) throw new IllegalArgumentException("Unknown runtime");
        String expectedHash = legacy ? "4274ab2d52b67e547786fd959474e020fd3052a34dbcd7da708f86bcf5e48225" : document.get("captureSha256").getAsString();
        if (!expectedHash.equalsIgnoreCase(currentProgram.getExecutableSHA256())) throw new IllegalStateException("Executable identity differs from mapping evidence");
        List<Map<String,Object>> records = new ArrayList<>();
        int existing=0, created=0, overlap=0, failed=0;
        for (JsonElement element : document.getAsJsonArray("records")) {
            monitor.checkCancelled();
            JsonObject map = element.getAsJsonObject();
            long value = map.get(legacy ? "legacyAddress" : "hostAddress").getAsLong();
            Address address = toAddr(value);
            Map<String,Object> record = new LinkedHashMap<>();
            record.put("address",address.toString());
            record.put("legacyAddress",map.get("legacyAddress").getAsLong());
            record.put("recordRva",map.get("recordRva").getAsLong());
            Function function = getFunctionAt(address);
            MemoryBlock block = currentProgram.getMemory().getBlock(address);
            if (function != null) { record.put("status","existing-entry"); existing++; }
            else if (block == null || !block.isExecute() || !block.isInitialized()) {
                record.put("status","outside-initialized-executable-memory"); failed++;
            } else if (getFunctionContaining(address) != null) {
                record.put("status","inside-another-function-review-required");
                record.put("containingEntry",getFunctionContaining(address).getEntryPoint().toString()); overlap++;
            } else {
                disassemble(address);
                if (getInstructionAt(address) != null) function = createFunction(address,null);
                if (function == null) { record.put("status","function-creation-failed"); failed++; }
                else { record.put("status","created-from-native-mapping"); created++; }
            }
            records.add(record);
        }
        Map<String,Object> report = new LinkedHashMap<>();
        report.put("sha256",currentProgram.getExecutableSHA256());
        report.put("runtime",args[1]); report.put("mappingRecords",records.size());
        report.put("existing",existing); report.put("created",created);
        report.put("overlapping",overlap); report.put("failed",failed); report.put("records",records);
        Files.writeString(Path.of(args[2]),new Gson().toJson(report)+"\n",StandardCharsets.UTF_8);
        println("NATIVE_MAP_SEEDS existing="+existing+" created="+created+" overlapping="+overlap+" failed="+failed);
    }
}
