// Recover only independently identified primary x64 unwind entries.
// Chained unwind ranges can describe fragments/data of an existing function;
// they are never silently turned into new functions here.
//@category BlindSoldier.Research
import com.google.gson.*;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.mem.MemoryBlock;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

public class SeedUnwindEngineFunctions extends GhidraScript {
    @Override public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 2) throw new IllegalArgumentException("SeedUnwindEngineFunctions <review-json> <report-json>");
        if (!"d7caa76e9cec08e495c0d4334deefaa76b2c9ef2a7dd8d3d0801c52c15873e2a".equalsIgnoreCase(currentProgram.getExecutableSHA256()))
            throw new IllegalStateException("This review belongs to a different loaded image");
        JsonObject document = JsonParser.parseString(Files.readString(Path.of(args[0]))).getAsJsonObject();
        long imageBase = currentProgram.getImageBase().getOffset();
        if (imageBase != document.get("imageBase").getAsLong()) throw new IllegalStateException("Image base differs");
        List<Map<String,Object>> rows = new ArrayList<>();
        int created = 0, skipped = 0, failed = 0;
        for (JsonElement element : document.getAsJsonArray("unassignedMissingEntries")) {
            JsonObject entry = element.getAsJsonObject();
            long begin = entry.get("beginRva").getAsLong();
            long end = entry.get("endRva").getAsLong();
            Address address = toAddr(imageBase + begin);
            int flags = Byte.toUnsignedInt(getByte(toAddr(imageBase + entry.get("unwindRva").getAsLong()))) >> 3;
            if (flags != entry.get("unwindFlags").getAsInt()) throw new IllegalStateException("Unwind flags differ");
            Map<String,Object> row = new LinkedHashMap<>();
            row.put("entry", address.toString()); row.put("unwindFlags", flags);
            row.put("beginRva", begin); row.put("endRva", end);
            if ((flags & 4) != 0) {
                row.put("status", "chained-range-retained-for-review"); skipped++;
            } else {
                MemoryBlock block = currentProgram.getMemory().getBlock(address);
                Function function = getFunctionAt(address);
                if (function != null) { row.put("status", "existing-entry"); skipped++; }
                else if (end <= begin || block == null || !block.isExecute() || !block.isInitialized()
                        || !block.contains(toAddr(imageBase + end - 1)) || getFunctionContaining(address) != null) {
                    row.put("status", "range-or-ownership-conflict"); failed++;
                } else {
                    disassemble(address);
                    if (getInstructionAt(address) != null) function = createFunction(address, null);
                    if (function == null) { row.put("status", "creation-failed"); failed++; }
                    else { row.put("status", "created-from-primary-unwind-entry"); created++; }
                }
            }
            rows.add(row);
        }
        Map<String,Object> report = new LinkedHashMap<>();
        report.put("sha256", currentProgram.getExecutableSHA256());
        report.put("created", created); report.put("skipped", skipped); report.put("failed", failed);
        report.put("records", rows);
        Files.writeString(Path.of(args[1]), new Gson().toJson(report) + "\n", StandardCharsets.UTF_8);
        println("UNWIND_SEEDS created=" + created + " skipped=" + skipped + " failed=" + failed);
    }
}
