// Read-only evidence for FFVII's field movie filename pointer table.

import java.nio.charset.StandardCharsets;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

public class DumpJunonMovieTableEvidence extends GhidraScript {
    private static final Object[][] REQUIRED_MOVIES = {
        { 0, "fship2.avi" },
        { 13, "junair_u.avi" },
        { 14, "junair_d.avi" },
        { 15, "junelein.avi" },
        { 16, "junelego.avi" },
        { 17, "junin_in.avi" },
        { 18, "junin_go.avi" },
        { 37, "junon.avi" },
        { 38, "hiwind0.avi" }
    };

    @Override
    public void run() throws Exception {
        Memory memory = currentProgram.getMemory();
        println("JUNON_MOVIE_TABLE_EVIDENCE program=" + currentProgram.getName());
        Address firstString = findString(memory, "fship2.avi");
        ReferenceIterator firstReferences =
            currentProgram.getReferenceManager().getReferencesTo(firstString);
        if (!firstReferences.hasNext()) {
            throw new Exception("The first movie filename has no pointer-table reference.");
        }
        Reference firstReference = firstReferences.next();
        Address table = firstReference.getFromAddress();
        if (firstReferences.hasNext()) {
            throw new Exception("The first movie filename has multiple references.");
        }
        println("pointerTable=" + table + " entrySize=4");

        for (Object[] required : REQUIRED_MOVIES) {
            monitor.checkCancelled();
            int movieId = (Integer) required[0];
            String expected = (String) required[1];
            Address pointerEntry = table.add((long) movieId * 4);
            long stringPointer = Integer.toUnsignedLong(memory.getInt(pointerEntry));
            Address stringAddress = toAddr(stringPointer);
            String actual = readString(memory, stringAddress);
            println("movie=" + movieId + " pointer=" + pointerEntry +
                " string=" + stringAddress + " name=" + actual);
            if (!expected.equals(actual)) {
                throw new Exception("Movie " + movieId + " expected " +
                    expected + " but found " + actual);
            }
        }
    }

    private Address findString(Memory memory, String value) throws Exception {
        Address result = memory.findBytes(
            currentProgram.getMinAddress(),
            (value + "\0").getBytes(StandardCharsets.US_ASCII),
            null,
            true,
            monitor);
        if (result == null) {
            throw new Exception("Could not locate " + value);
        }
        return result;
    }

    private String readString(Memory memory, Address address) throws Exception {
        byte[] bytes = new byte[64];
        memory.getBytes(address, bytes);
        int length = 0;
        while (length < bytes.length && bytes[length] != 0) {
            length++;
        }
        if (length == bytes.length) {
            throw new Exception("Unterminated movie filename at " + address);
        }
        return new String(bytes, 0, length, StandardCharsets.US_ASCII);
    }
}
