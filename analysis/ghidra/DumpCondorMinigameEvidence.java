// Locates the Fort Condor (module 9) minigame handler in the legacy x86
// ff7_en.exe and reports how it produces on-screen text, so the accessibility
// mod can decide whether it can reuse the existing text-draw hooks or needs a
// state reader instead.
//
// @category FF7.BlindSoldier

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.Program;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.TreeSet;

public class DumpCondorMinigameEvidence extends GhidraScript {

    private static final long ADDRESS_CURRENT_MODULE = 0x00CBF9DCL;

    // The three text-draw entry points the mod already hooks.
    private static final long[] TEXT_DRAW_TARGETS = {
        0x0072D333L,
        0x0072F96EL,
        0x0072F9F4L
    };

    @Override
    protected void run() throws Exception {
        Program program = currentProgram;
        println("program: " + program.getName());

        Address moduleAddress = addr(ADDRESS_CURRENT_MODULE);
        println("=== references to the current-module byte 0x" +
            Long.toHexString(ADDRESS_CURRENT_MODULE) + " ===");
        Set<Function> moduleReaders = new HashSet<>();
        ReferenceIterator refs = program.getReferenceManager().getReferencesTo(moduleAddress);
        while (refs.hasNext()) {
            Reference reference = refs.next();
            Address from = reference.getFromAddress();
            Function owner = getFunctionContaining(from);
            String ownerName = owner == null ? "<none>" : owner.getName() + "@" + owner.getEntryPoint();
            println("  " + from + "  " + reference.getReferenceType() + "  in " + ownerName);
            if (owner != null) {
                moduleReaders.add(owner);
            }
        }

        println("");
        println("=== functions that both read the module byte and compare against 9 ===");
        for (Function function : moduleReaders) {
            List<String> hits = comparisonsAgainst(function, 9);
            if (!hits.isEmpty()) {
                println("  " + function.getName() + " @ " + function.getEntryPoint());
                for (String hit : hits) {
                    println("      " + hit);
                }
            }
        }

        println("");
        println("=== callers of each text-draw entry point ===");
        for (long target : TEXT_DRAW_TARGETS) {
            Address targetAddress = addr(target);
            Function targetFunction = getFunctionContaining(targetAddress);
            println("  target 0x" + Long.toHexString(target) + " -> " +
                (targetFunction == null
                    ? "<no function>"
                    : targetFunction.getName() + " @ " + targetFunction.getEntryPoint()));
            if (targetFunction == null) {
                continue;
            }

            Set<String> callers = new TreeSet<>();
            for (Function caller : targetFunction.getCallingFunctions(monitor)) {
                callers.add(caller.getName() + " @ " + caller.getEntryPoint());
            }
            for (String caller : callers) {
                println("      called by " + caller);
            }
        }

        println("");
        println("=== module-9 handler candidates: reachability to the text-draw targets ===");
        for (Function function : moduleReaders) {
            if (comparisonsAgainst(function, 9).isEmpty()) {
                continue;
            }
            for (long target : TEXT_DRAW_TARGETS) {
                Function targetFunction = getFunctionContaining(addr(target));
                if (targetFunction == null) {
                    continue;
                }

                List<String> path = findCallPath(function, targetFunction, 6);
                println("  " + function.getName() + " -> 0x" + Long.toHexString(target) + ": " +
                    (path == null ? "no path within 6 calls" : String.join(" -> ", path)));
            }
        }

        println("");
        println("=== indirect dispatch tables referencing many handlers ===");
        dumpJumpTableCandidates();
    }

    private Address addr(long value) {
        return currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(value);
    }

    private List<String> comparisonsAgainst(Function function, int value) {
        List<String> hits = new ArrayList<>();
        Instruction instruction = getInstructionAt(function.getEntryPoint());
        while (instruction != null && function.getBody().contains(instruction.getAddress())) {
            String mnemonic = instruction.getMnemonicString().toLowerCase();
            if (mnemonic.equals("cmp") || mnemonic.equals("sub") || mnemonic.equals("mov")) {
                String text = instruction.toString();
                if (text.contains("0x" + Integer.toHexString(value)) ||
                    text.matches(".*[ ,]" + value + "\\b.*")) {
                    hits.add(instruction.getAddress() + "  " + text);
                }
            }
            instruction = instruction.getNext();
        }
        return hits;
    }

    private List<String> findCallPath(Function from, Function to, int maxDepth) {
        Deque<List<Function>> queue = new ArrayDeque<>();
        Set<Function> seen = new HashSet<>();
        List<Function> start = new ArrayList<>();
        start.add(from);
        queue.add(start);
        seen.add(from);

        while (!queue.isEmpty()) {
            List<Function> path = queue.poll();
            Function tail = path.get(path.size() - 1);
            if (tail.equals(to)) {
                List<String> names = new ArrayList<>();
                for (Function step : path) {
                    names.add(step.getName());
                }
                return names;
            }

            if (path.size() > maxDepth) {
                continue;
            }

            for (Function callee : tail.getCalledFunctions(monitor)) {
                if (!seen.add(callee)) {
                    continue;
                }
                List<Function> next = new ArrayList<>(path);
                next.add(callee);
                queue.add(next);
            }
        }

        return null;
    }

    private void dumpJumpTableCandidates() {
        // A module dispatcher is a run of consecutive pointers into code. Report
        // any run of eight or more so the module table can be identified by eye.
        Address address = currentProgram.getMinAddress();
        Address max = currentProgram.getMaxAddress();
        int run = 0;
        Address runStart = null;
        while (address != null && address.compareTo(max) < 0) {
            Long pointer = readPointer(address);
            boolean isCodePointer = pointer != null && getFunctionAt(addr(pointer)) != null;
            if (isCodePointer) {
                if (run == 0) {
                    runStart = address;
                }
                run++;
            } else {
                if (run >= 8) {
                    println("  table at " + runStart + " with " + run + " code pointers");
                }
                run = 0;
            }

            try {
                address = address.add(4);
            } catch (Exception stop) {
                break;
            }
        }

        if (run >= 8) {
            println("  table at " + runStart + " with " + run + " code pointers");
        }
    }

    private Long readPointer(Address address) {
        try {
            return Integer.toUnsignedLong(getInt(address));
        } catch (Exception unreadable) {
            return null;
        }
    }
}
