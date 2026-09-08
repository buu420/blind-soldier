// Read-only evidence linking the selected Gold Saucer FMVs to native movie IDs.
import java.nio.charset.StandardCharsets;
import java.util.HashSet;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.symbol.ReferenceIterator;

public class DumpGoldSaucerMovieTableEvidence extends GhidraScript {
    public void run() throws Exception {
        Memory memory = currentProgram.getMemory();
        Address first = memory.findBytes(currentProgram.getMinAddress(),
            "fship2.avi\0".getBytes(StandardCharsets.US_ASCII), null, true, monitor);
        if (first == null) throw new Exception("Movie table anchor missing");
        ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(first);
        if (!references.hasNext()) throw new Exception("Movie table reference missing");
        Address table = references.next().getFromAddress();
        if (references.hasNext()) throw new Exception("Ambiguous movie table reference");
        println("GOLD_SAUCER_MOVIE_TABLE program=" + currentProgram.getName() + " table=" + table);
        HashSet<String> found = new HashSet<>();
        for (int index = 0; index < 128; index++) {
            monitor.checkCancelled();
            Address entry = table.add(index * 4L);
            Address pointer = toAddr(Integer.toUnsignedLong(memory.getInt(entry)));
            if (!memory.contains(pointer)) break;
            byte[] bytes = new byte[64];
            memory.getBytes(pointer, bytes);
            int length = 0;
            while (length < bytes.length && bytes[length] != 0) length++;
            if (length == bytes.length) break;
            String name = new String(bytes, 0, length, StandardCharsets.US_ASCII);
            if (!name.endsWith(".avi")) break;
            if (name.startsWith("gold")) {
                println("movie=" + index + " entry=" + entry + " name=" + name);
                found.add(name);
            }
        }
        if (!found.contains("gold1.avi") || !found.contains("gold6.avi"))
            throw new Exception("Gold Saucer arrival movies not found in validated table");
    }
}
