// Traces the original FFVII PC Fort Condor module from the anchors documented
// by FFNx. This script is read-only: it prints call relationships, decompiled
// functions, and static data references without renaming or modifying the
// analyzed program.
//
// FFNx resolves:
//   condor_main_loop -> call +0x5B = FUN_005F4971 (reset game object)
//   condor_main_loop -> call +0x69 = FUN_005F5042 (per-frame work)
//   Condor enter path -> FUN_005F7756 -> FUN_005F4273 -> FUN_005F342C
//
// @category FF7.BlindSoldier

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;
import ghidra.program.model.symbol.Reference;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.Deque;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeSet;

public class TraceCondorModuleState extends GhidraScript {
    private static final long CONDOR_LOAD_TEXTURES = 0x005F342CL;
    private static final long CONDOR_RESET_GAME_OBJECT = 0x005F4971L;
    private static final long CONDOR_TICK_ANCHOR = 0x005F5042L;
    private static final long CONDOR_ENTER_HELPER = 0x005F7756L;

    private static final long IMAGE_DATA_MIN = 0x00800000L;
    private static final int ANCESTOR_DEPTH = 4;
    private static final int TICK_DESCENDANT_DEPTH = 2;

    private DecompInterface decompiler;

    @Override
    public void run() throws Exception {
        println("CONDOR_MODULE_STATE_TRACE program=" + currentProgram.getName());
        println("  language=" + currentProgram.getLanguageID());

        decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            Function loader = requireFunction(CONDOR_LOAD_TEXTURES, "CONDOR_LOAD_TEXTURES");
            Function reset = requireFunction(CONDOR_RESET_GAME_OBJECT, "CONDOR_RESET_GAME_OBJECT");
            Function tick = requireFunction(CONDOR_TICK_ANCHOR, "CONDOR_TICK_ANCHOR");
            Function enterHelper = requireFunction(CONDOR_ENTER_HELPER, "CONDOR_ENTER_HELPER");

            printCallers("LOADER_CALLERS", loader);
            printAncestorTree("LOADER_ANCESTORS", loader, ANCESTOR_DEPTH);
            printCallers("RESET_CALLERS", reset);
            printCallers("TICK_CALLERS", tick);
            printCallers("ENTER_HELPER_CALLERS", enterHelper);

            Set<Function> mainLoopCandidates = intersection(
                reset.getCallingFunctions(monitor),
                tick.getCallingFunctions(monitor));
            println("=== CONDOR_MAIN_LOOP_CANDIDATES count=" + mainLoopCandidates.size() + " ===");
            printFunctionNames(mainLoopCandidates);

            Set<Function> decompileTargets = new LinkedHashSet<>();
            decompileTargets.add(loader);
            decompileTargets.add(tick);
            decompileTargets.add(enterHelper);
            decompileTargets.addAll(loader.getCallingFunctions(monitor));
            decompileTargets.addAll(mainLoopCandidates);

            Set<Function> tickGraph = collectDescendants(tick, TICK_DESCENDANT_DEPTH);
            decompileTargets.addAll(tickGraph);

            println("=== TICK_CALL_GRAPH depth=" + TICK_DESCENDANT_DEPTH +
                " count=" + tickGraph.size() + " ===");
            printFunctionNames(tickGraph);

            println("=== DATA_REFERENCES_FROM_TICK_GRAPH ===");
            printDataReferences(tickGraph);

            println("=== DECOMPILED_CONDOR_FUNCTIONS count=" +
                decompileTargets.size() + " ===");
            for (Function function : sorted(decompileTargets)) {
                printDecompile(function);
            }
        }
        finally {
            decompiler.dispose();
        }
    }

    private Function requireFunction(long raw, String label) {
        Address address = toAddr(raw);
        Function function = getFunctionAt(address);
        if (function == null) {
            function = getFunctionContaining(address);
        }
        if (function == null) {
            throw new IllegalStateException(label + " missing at " + address);
        }
        println(label + "=" + function.getName() + "@" + function.getEntryPoint());
        return function;
    }

    private void printCallers(String label, Function function) throws Exception {
        Set<Function> callers = function.getCallingFunctions(monitor);
        println("=== " + label + " target=" + function.getName() +
            " count=" + callers.size() + " ===");
        for (Function caller : sorted(callers)) {
            List<String> sites = new ArrayList<>();
            for (Reference reference : getReferencesTo(function.getEntryPoint())) {
                if (caller.getBody().contains(reference.getFromAddress())) {
                    sites.add(reference.getFromAddress().toString());
                }
            }
            println("  " + caller.getName() + "@" + caller.getEntryPoint() +
                " sites=" + String.join(",", sites));
        }
    }

    private void printAncestorTree(String label, Function start, int maxDepth)
            throws Exception {
        println("=== " + label + " maxDepth=" + maxDepth + " ===");
        Deque<FunctionPath> queue = new ArrayDeque<>();
        Set<String> emitted = new LinkedHashSet<>();
        queue.add(new FunctionPath(start, start.getName(), 0));
        while (!queue.isEmpty()) {
            monitor.checkCancelled();
            FunctionPath path = queue.removeFirst();
            if (path.depth >= maxDepth) {
                continue;
            }
            for (Function caller : sorted(path.function.getCallingFunctions(monitor))) {
                String nextPath = caller.getName() + " -> " + path.text;
                if (emitted.add(nextPath)) {
                    println("  depth=" + (path.depth + 1) + " " + nextPath);
                    queue.addLast(new FunctionPath(caller, nextPath, path.depth + 1));
                }
            }
        }
    }

    private Set<Function> collectDescendants(Function start, int maxDepth)
            throws Exception {
        Set<Function> result = new LinkedHashSet<>();
        Deque<FunctionDepth> queue = new ArrayDeque<>();
        result.add(start);
        queue.add(new FunctionDepth(start, 0));
        while (!queue.isEmpty()) {
            monitor.checkCancelled();
            FunctionDepth item = queue.removeFirst();
            if (item.depth >= maxDepth) {
                continue;
            }
            for (Function called : sorted(item.function.getCalledFunctions(monitor))) {
                if (result.add(called)) {
                    queue.addLast(new FunctionDepth(called, item.depth + 1));
                }
            }
        }
        return result;
    }

    private void printDataReferences(Set<Function> functions) throws Exception {
        Map<String, Set<String>> references = new LinkedHashMap<>();
        for (Function function : sorted(functions)) {
            InstructionIterator instructions = currentProgram.getListing()
                .getInstructions(function.getBody(), true);
            while (instructions.hasNext()) {
                monitor.checkCancelled();
                Instruction instruction = instructions.next();
                for (Reference reference : instruction.getReferencesFrom()) {
                    if (reference.getReferenceType().isFlow()) {
                        continue;
                    }
                    Address target = reference.getToAddress();
                    if (!target.isMemoryAddress() ||
                        Long.compareUnsigned(target.getOffset(), IMAGE_DATA_MIN) < 0) {
                        continue;
                    }
                    String key = target.toString();
                    references.computeIfAbsent(key, ignored -> new TreeSet<>())
                        .add(function.getName() + "@" + instruction.getAddress() +
                            " " + reference.getReferenceType() + " " + instruction);
                }
            }
        }
        for (Map.Entry<String, Set<String>> entry : references.entrySet()) {
            println("GLOBAL " + entry.getKey());
            for (String use : entry.getValue()) {
                println("  " + use);
            }
        }
    }

    private void printDecompile(Function function) throws Exception {
        monitor.checkCancelled();
        println("FUNCTION " + function.getName() + " " + function.getEntryPoint());
        DecompileResults result = decompiler.decompileFunction(function, 90, monitor);
        if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
            println(result.getDecompiledFunction().getC());
        }
        else {
            println("  decompileFailed=" + result.getErrorMessage());
        }
    }

    private void printFunctionNames(Set<Function> functions) {
        for (Function function : sorted(functions)) {
            println("  " + function.getName() + "@" + function.getEntryPoint());
        }
    }

    private Set<Function> intersection(Set<Function> left, Set<Function> right) {
        Set<Function> result = new LinkedHashSet<>(left);
        result.retainAll(right);
        return result;
    }

    private List<Function> sorted(Set<Function> functions) {
        List<Function> result = new ArrayList<>(functions);
        result.sort(Comparator.comparing(function -> function.getEntryPoint().getOffset()));
        return result;
    }

    private static final class FunctionDepth {
        private final Function function;
        private final int depth;

        private FunctionDepth(Function function, int depth) {
            this.function = function;
            this.depth = depth;
        }
    }

    private static final class FunctionPath {
        private final Function function;
        private final String text;
        private final int depth;

        private FunctionPath(Function function, String text, int depth) {
            this.function = function;
            this.text = text;
            this.depth = depth;
        }
    }
}
