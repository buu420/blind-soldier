// Machine-readable Ghidra evidence for an optional, locally available FFVII
// host. Game executables are identity evidence only: registry behavior in the
// original game is outside Blind Soldier's portable-binary policy.

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;

import ghidra.app.script.GhidraScript;
import ghidra.app.util.bin.FileByteProvider;
import ghidra.app.util.bin.format.pe.FileHeader;
import ghidra.app.util.bin.format.pe.NTHeader;
import ghidra.app.util.bin.format.pe.PortableExecutable;
import ghidra.app.util.bin.format.pe.PortableExecutable.SectionLayout;
import java.nio.file.AccessMode;

public class BlindSoldierNativeEvidence extends GhidraScript {
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
        String json = "{\n" +
            "  \"schemaVersion\": 1,\n" +
            "  \"marker\": \"BLIND_SOLDIER_GHIDRA_EVIDENCE\",\n" +
            "  \"kind\": \"" + escape(kind) + "\",\n" +
            "  \"program\": \"" + escape(executable.getAbsolutePath()) + "\",\n" +
            "  \"sha256\": \"" + sha256(executable.toPath()) + "\",\n" +
            "  \"machine\": " + machine + ",\n" +
            "  \"required\": { \"HostIdentity\": " +
                (machine == expectedMachine) + " },\n" +
            "  \"forbidden\": [],\n" +
            "  \"exports\": [],\n" +
            "  \"tool\": \"Ghidra headless\"\n" +
            "}\n";
        Files.createDirectories(report.getParent());
        Files.writeString(report, json, StandardCharsets.UTF_8);
        println("BLIND_SOLDIER_GHIDRA_EVIDENCE " + report);
        if (machine != expectedMachine) {
            throw new Exception("Host PE machine does not match the requested evidence kind.");
        }
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
