// Headless Ghidra evidence collector for FFVII executable identities used by
// Blind Soldier's registry-free bootstrap.

import java.io.File;
import java.nio.file.AccessMode;
import java.util.Set;
import java.util.TreeSet;

import ghidra.app.script.GhidraScript;
import ghidra.app.util.bin.FileByteProvider;
import ghidra.app.util.bin.format.pe.DataDirectory;
import ghidra.app.util.bin.format.pe.FileHeader;
import ghidra.app.util.bin.format.pe.NTHeader;
import ghidra.app.util.bin.format.pe.OptionalHeader;
import ghidra.app.util.bin.format.pe.PortableExecutable;
import ghidra.app.util.bin.format.pe.PortableExecutable.SectionLayout;
import ghidra.app.util.bin.format.pe.ResourceDataDirectory;
import ghidra.app.util.bin.format.pe.SectionHeader;
import ghidra.app.util.bin.format.pe.resource.ResourceInfo;
import ghidra.program.model.address.Address;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;

public class BlindSoldierHostEvidence extends GhidraScript {
    private static final long[] LEGACY_CODE_ADDRESSES = {
        0x0042D833L, // BattleHookAddressResolver.AddressUpdateDisplayTextCall
        0x0060BACFL, // FieldOpcodeParameterReader.AddressFieldInitEvent
        0x0063C17FL  // FieldRunStateReader.AddressFieldLoopSub
    };

    @Override
    public void run() throws Exception {
        println("BLIND_SOLDIER_HOST_EVIDENCE");
        println("program=" + currentProgram.getName());
        println("format=" + currentProgram.getExecutableFormat());
        println("language=" + currentProgram.getLanguageID());
        println("compiler=" + currentProgram.getCompilerSpec().getCompilerSpecID());
        println("imageBase=0x" + Long.toHexString(currentProgram.getImageBase().getOffset()));

        emitPortableExecutableEvidence();

        Memory memory = currentProgram.getMemory();
        for (MemoryBlock block : memory.getBlocks()) {
            monitor.checkCancelled();
            println("section=" + escape(block.getName()) + "|0x" +
                Long.toHexString(block.getStart().getOffset()) + "|0x" +
                Long.toHexString(block.getSize()) + "|" +
                (block.isRead() ? "R" : "-") +
                (block.isWrite() ? "W" : "-") +
                (block.isExecute() ? "X" : "-"));
        }

        Set<String> imports = new TreeSet<>();
        Set<String> importedSymbols = new TreeSet<>();
        SymbolIterator symbols = currentProgram.getSymbolTable().getExternalSymbols();
        while (symbols.hasNext()) {
            monitor.checkCancelled();
            Symbol symbol = symbols.next();
            String parent = symbol.getParentNamespace() == null
                ? "<unknown>"
                : symbol.getParentNamespace().getName();
            imports.add(parent);
            importedSymbols.add(parent + "|" + symbol.getName());
        }
        for (String value : imports) {
            println("importModule=" + escape(value));
        }
        for (String value : importedSymbols) {
            println("importSymbol=" + escape(value));
        }

        if (currentProgram.getLanguage().getLanguageDescription().getSize() == 32) {
            for (long value : LEGACY_CODE_ADDRESSES) {
                Address address = toAddr(value);
                byte[] bytes = new byte[16];
                int count = memory.getBytes(address, bytes);
                if (count != bytes.length) {
                    throw new Exception("Unable to read 16 bytes at " + address);
                }
                println("signature=0x" + Long.toHexString(value) + "|" + toHex(bytes));
            }
        }
    }

    private void emitPortableExecutableEvidence() throws Exception {
        File executable = new File(currentProgram.getExecutablePath());
        try (FileByteProvider provider =
                new FileByteProvider(executable, null, AccessMode.READ)) {
            PortableExecutable pe =
                new PortableExecutable(provider, SectionLayout.FILE, false, false);
            NTHeader ntHeader = pe.getNTHeader();
            if (ntHeader == null || ntHeader.getOptionalHeader() == null) {
                throw new Exception("Ghidra could not parse the PE headers.");
            }

            FileHeader fileHeader = ntHeader.getFileHeader();
            OptionalHeader optionalHeader = ntHeader.getOptionalHeader();
            println("machine=0x" +
                Integer.toHexString(Short.toUnsignedInt(fileHeader.getMachine())));
            println("peImageBase=0x" + Long.toHexString(optionalHeader.getImageBase()));
            for (SectionHeader section : fileHeader.getSectionHeaders()) {
                monitor.checkCancelled();
                println("peSection=" + escape(section.getName()) + "|rva=0x" +
                    Integer.toUnsignedString(section.getVirtualAddress(), 16) +
                    "|virtualSize=0x" +
                    Integer.toUnsignedString(section.getVirtualSize(), 16) +
                    "|rawOffset=0x" +
                    Integer.toUnsignedString(section.getPointerToRawData(), 16) +
                    "|rawSize=0x" +
                    Integer.toUnsignedString(section.getSizeOfRawData(), 16) +
                    "|flags=0x" +
                    Integer.toUnsignedString(section.getCharacteristics(), 16));
            }

            boolean hasManifest = false;
            DataDirectory[] directories = optionalHeader.getDataDirectories();
            if (directories != null &&
                    directories.length > OptionalHeader.IMAGE_DIRECTORY_ENTRY_RESOURCE &&
                    directories[OptionalHeader.IMAGE_DIRECTORY_ENTRY_RESOURCE]
                        instanceof ResourceDataDirectory) {
                ResourceDataDirectory resources = (ResourceDataDirectory)
                    directories[OptionalHeader.IMAGE_DIRECTORY_ENTRY_RESOURCE];
                if (resources.getVirtualAddress() != 0 && resources.getSize() != 0) {
                    resources.parse();
                    for (ResourceInfo resource : resources.getResources()) {
                        if (resource.getTypeID() == ResourceDataDirectory.RT_MANIFEST) {
                            hasManifest = true;
                            break;
                        }
                    }
                }
            }
            println("embeddedManifest=" + hasManifest);
        }
    }

    private static String toHex(byte[] bytes) {
        StringBuilder result = new StringBuilder(bytes.length * 2);
        for (byte value : bytes) {
            result.append(String.format("%02X", value & 0xFF));
        }
        return result.toString();
    }

    private static String escape(String value) {
        return value.replace("\\", "\\\\").replace("|", "\\|");
    }
}
