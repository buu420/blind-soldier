//Dumps raw bytes at an address, for reading native tables.
//@category BlindSoldier
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;

public class DumpGoldSaucerBytes extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        Address start = currentProgram.getAddressFactory().getAddress(args[0]);
        int length = Integer.decode(args[1]);
        byte[] data = new byte[length];
        currentProgram.getMemory().getBytes(start, data);
        StringBuilder line = new StringBuilder();
        for (int i = 0; i < length; i++) {
            if (i % 16 == 0) {
                if (line.length() > 0) {
                    println(line.toString());
                }
                line = new StringBuilder(String.format("%08x:", start.getOffset() + i));
            }
            line.append(String.format(" %02x", data[i] & 0xff));
        }
        println(line.toString());
    }
}
