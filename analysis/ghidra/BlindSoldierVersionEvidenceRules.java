import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Collection;
import java.util.Deque;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;

public final class BlindSoldierVersionEvidenceRules {
    private static final int CLUSTER_DEPTH = 4;
    private static final int WORKER_CLUSTER_DEPTH = 6;
    private static final Set<String> FORBIDDEN_IMPORTS = Set.of(
        "RegCreateKeyA", "RegCreateKeyW", "RegCreateKeyExA", "RegCreateKeyExW",
        "RegCreateKeyTransactedA", "RegCreateKeyTransactedW",
        "RegSetKeyValueA", "RegSetKeyValueW", "RegSetValueA", "RegSetValueW",
        "RegSetValueExA", "RegSetValueExW", "RegDeleteKeyA", "RegDeleteKeyW",
        "RegDeleteKeyExA", "RegDeleteKeyExW", "RegDeleteKeyValueA",
        "RegDeleteKeyValueW", "RegDeleteValueA", "RegDeleteValueW",
        "RegRenameKey", "RegReplaceKeyA", "RegReplaceKeyW", "RegRestoreKeyA",
        "RegRestoreKeyW", "RegLoadKeyA", "RegLoadKeyW", "RegUnLoadKeyA",
        "RegUnLoadKeyW", "RegSaveKeyA", "RegSaveKeyW", "RegSaveKeyExA",
        "RegSaveKeyExW", "RegCopyTreeA", "RegCopyTreeW", "RegOverridePredefKey",
        "RegDeleteKeyTransactedA", "RegDeleteKeyTransactedW",
        "RegDeleteTreeA", "RegDeleteTreeW", "RegSetKeySecurity",
        "RegDisableReflectionKey", "RegEnableReflectionKey",
        "SHSetValueA", "SHSetValueW", "SHDeleteKeyA", "SHDeleteKeyW",
        "SHDeleteValueA", "SHDeleteValueW", "SHRegWriteUSValueA",
        "SHRegWriteUSValueW", "VirtualAllocEx", "VirtualAllocExNuma",
        "VirtualProtectEx", "WriteProcessMemory", "CreateRemoteThread",
        "CreateRemoteThreadEx", "NtAllocateVirtualMemory",
        "ZwAllocateVirtualMemory", "NtWriteVirtualMemory",
        "ZwWriteVirtualMemory", "NtProtectVirtualMemory",
        "ZwProtectVirtualMemory", "NtCreateThreadEx", "RtlCreateUserThread",
        "QueueUserAPC", "QueueUserAPC2", "NtQueueApcThread",
        "SetThreadContext", "Wow64SetThreadContext", "SetWindowsHookExA",
        "SetWindowsHookExW", "OpenProcess", "DebugActiveProcess");

    private BlindSoldierVersionEvidenceRules() {}

    public static LinkedHashMap<String, Boolean> evaluate(ProgramFacts facts) {
        LinkedHashMap<String, Boolean> claims = new LinkedHashMap<>();
        claims.put("SystemVersionLoaderCluster", anyFunction(facts, function ->
            function.hasSymbols("GetSystemDirectoryW", "LoadLibraryW",
                "LoadLibraryExW") && function.hasString("version.dll")));
        claims.put("VersionCacheValidationCluster", anyStringCluster(facts,
            "version-system-x86-", CLUSTER_DEPTH, cluster ->
                clusterHasString(facts, cluster, "NativeCache") &&
                clusterHasSymbols(facts, cluster,
                    "GetFileInformationByHandleEx", "CopyFileW", "MoveFileExW",
                    "DeleteFileW", "BCryptCreateHash", "BCryptHashData",
                    "BCryptFinishHash") &&
                clusterHasScalars(facts, cluster, 332L, 8192L) &&
                clusterHasSymbolAndScalar(facts, cluster, "MoveFileExW", 8L)));
        claims.put("AppLoaderSignatureCluster", anyFunctionCluster(facts,
            function -> function.hasStrings("dinput.dll",
                "AppProxy.runtimeconfig.json", "AppProxy.dll", "AppWrapper.dll",
                "nethost.dll"), 2, cluster -> clusterHasSymbols(facts, cluster,
                    "GetFileAttributesW", "GetModuleHandleW")));
        claims.put("AppLoaderMarkerParser", anyFunction(facts, function ->
            function.hasStrings("AppLoader init log",
                "AppLoader started successfully")));
        claims.put("AppLoaderTimeoutStateCluster", anyScalarCluster(facts,
            120000L, CLUSTER_DEPTH, cluster ->
                clusterHasString(facts, cluster,
                    "Timed out waiting for AppLoader readiness.") &&
                clusterHasString(facts, cluster, "AppLoader readiness state=") &&
                clusterHasString(facts, cluster, "waiting-for-success") &&
                clusterHasString(facts, cluster, "ready-seventh-heaven")));
        claims.put("SupportedHostNameValidation", anyFunction(facts, function ->
            function.hasStrings("ff7_en.exe", "ff7.exe",
                "Legacy host must be named ff7_en.exe or ff7.exe.")));
        claims.put("PackageRootBoundaryValidation", anyFunction(facts, function ->
            function.hasStrings("within four parent directories",
                "More than one complete", "outside the discovered package root")));
        claims.put("VersionWorkerAndPortableBrokerPrimitives",
            facts.hasImports("CreateThread", "CreateProcessW") &&
            anyStringCluster(facts, "Blind-Soldier-Bootstrap-x86.exe",
                WORKER_CLUSTER_DEPTH, cluster ->
                    clusterHasString(facts, cluster,
                        "Local\\BlindSoldier.Ready.")) &&
            anyFunction(facts, function ->
                function.hasSymbol("CreateProcessW") &&
                function.hasStrings(
                    "CreateProcessW(x86 accessibility broker)",
                    "x86 broker started:")));
        claims.put("NoWinmmForwardingSurface",
            !facts.hasImport("GetSystemWow64DirectoryW") &&
            !hasWinmmExport(facts.exports));
        claims.put("NoEmbeddedExternalRuntime",
            !facts.nestedPortableExecutable && !facts.zipArchive);
        return claims;
    }

    public static List<String> forbiddenImports(ProgramFacts facts) {
        List<String> found = new ArrayList<>();
        for (String forbidden : FORBIDDEN_IMPORTS) {
            if (facts.hasImport(forbidden)) found.add(forbidden);
        }
        found.sort(String.CASE_INSENSITIVE_ORDER);
        return found;
    }

    private interface FunctionPredicate { boolean test(FunctionFacts function); }
    private interface ClusterPredicate { boolean test(Set<String> cluster); }

    private static boolean anyFunction(ProgramFacts facts,
            FunctionPredicate predicate) {
        for (FunctionFacts function : facts.functions.values()) {
            if (predicate.test(function)) return true;
        }
        return false;
    }

    private static boolean anyFunctionCluster(ProgramFacts facts,
            FunctionPredicate anchor, int depth, ClusterPredicate predicate) {
        for (FunctionFacts function : facts.functions.values()) {
            if (anchor.test(function) && predicate.test(
                    connectedCluster(facts, List.of(function.id), depth))) {
                return true;
            }
        }
        return false;
    }

    private static boolean anyStringCluster(ProgramFacts facts, String anchor,
            int depth, ClusterPredicate predicate) {
        return anyFunctionCluster(facts, function -> function.hasString(anchor),
            depth, predicate);
    }

    private static boolean anyScalarCluster(ProgramFacts facts, long anchor,
            int depth, ClusterPredicate predicate) {
        return anyFunctionCluster(facts, function -> function.scalars.contains(anchor),
            depth, predicate);
    }

    private static Set<String> connectedCluster(ProgramFacts facts,
            Collection<String> starts, int maximumDepth) {
        Set<String> found = new LinkedHashSet<>();
        Deque<Visit> pending = new ArrayDeque<>();
        for (String start : starts) pending.addLast(new Visit(start, 0));
        while (!pending.isEmpty()) {
            Visit visit = pending.removeFirst();
            if (visit.depth > maximumDepth || !found.add(visit.id)) continue;
            FunctionFacts current = facts.functions.get(visit.id);
            if (current == null || visit.depth == maximumDepth) continue;
            for (String callee : current.calls) {
                pending.addLast(new Visit(callee, visit.depth + 1));
            }
            for (FunctionFacts candidate : facts.functions.values()) {
                if (candidate.calls.contains(visit.id)) {
                    pending.addLast(new Visit(candidate.id, visit.depth + 1));
                }
            }
        }
        return found;
    }

    private static boolean clusterHasSymbols(ProgramFacts facts,
            Set<String> cluster, String... symbols) {
        for (String symbol : symbols) {
            if (!clusterHasSymbol(facts, cluster, symbol)) return false;
        }
        return true;
    }

    private static boolean clusterHasSymbol(ProgramFacts facts,
            Set<String> cluster, String symbol) {
        for (String id : cluster) {
            FunctionFacts function = facts.functions.get(id);
            if (function != null && function.hasSymbol(symbol)) return true;
        }
        return false;
    }

    private static boolean clusterHasString(ProgramFacts facts,
            Set<String> cluster, String value) {
        for (String id : cluster) {
            FunctionFacts function = facts.functions.get(id);
            if (function != null && function.hasString(value)) return true;
        }
        return false;
    }

    private static boolean clusterHasScalars(ProgramFacts facts,
            Set<String> cluster, long... values) {
        for (long value : values) {
            boolean found = false;
            for (String id : cluster) {
                FunctionFacts function = facts.functions.get(id);
                if (function != null && function.scalars.contains(value)) {
                    found = true;
                    break;
                }
            }
            if (!found) return false;
        }
        return true;
    }

    private static boolean clusterHasSymbolAndScalar(ProgramFacts facts,
            Set<String> cluster, String symbol, long scalar) {
        for (String id : cluster) {
            FunctionFacts function = facts.functions.get(id);
            if (function != null && function.hasSymbol(symbol) &&
                    function.scalars.contains(scalar)) return true;
        }
        return false;
    }

    private static boolean hasWinmmExport(Set<String> exports) {
        for (String value : exports) {
            String name = value.toLowerCase(Locale.ROOT);
            if (name.startsWith("wave") || name.startsWith("midi") ||
                    name.startsWith("mixer") || name.startsWith("joy") ||
                    name.startsWith("timeget") || name.startsWith("timeset") ||
                    name.startsWith("mmio") || name.startsWith("mci")) return true;
        }
        return false;
    }

    static String normalizeSymbol(String value) {
        if (value == null) return "";
        String result = value.trim();
        int separator = Math.max(result.lastIndexOf('!'),
            result.lastIndexOf("::"));
        if (separator >= 0) result = result.substring(separator +
            (result.charAt(separator) == '!' ? 1 : 2));
        for (String prefix : new String[] {"__imp__", "__imp_", "_imp__", "imp_"}) {
            if (result.regionMatches(true, 0, prefix, 0, prefix.length())) {
                result = result.substring(prefix.length());
                break;
            }
        }
        while (result.startsWith("_")) result = result.substring(1);
        int suffix = result.lastIndexOf('@');
        if (suffix > 0 && result.substring(suffix + 1).matches("[0-9]+")) {
            result = result.substring(0, suffix);
        }
        return result;
    }

    static String matchImportedSymbol(String candidate,
            Collection<String> importNames) {
        if (candidate == null || importNames == null) return null;
        String raw = candidate.trim();
        String normalized = normalizeSymbol(raw);
        boolean decorated = startsWithIgnoreCase(raw, "PTR_") ||
            startsWithIgnoreCase(raw, "__imp__") ||
            startsWithIgnoreCase(raw, "__imp_") ||
            startsWithIgnoreCase(raw, "_imp__") ||
            startsWithIgnoreCase(raw, "imp_");
        if (startsWithIgnoreCase(normalized, "PTR_")) {
            normalized = normalizeSymbol(normalized.substring(4));
            decorated = true;
        }
        for (String imported : importNames) {
            String canonical = normalizeSymbol(imported);
            if (normalized.equalsIgnoreCase(canonical)) return canonical;
            if (!decorated || normalized.length() <= canonical.length() + 1 ||
                    !normalized.regionMatches(true, 0, canonical, 0,
                        canonical.length()) ||
                    normalized.charAt(canonical.length()) != '_') continue;
            String suffix = normalized.substring(canonical.length() + 1);
            boolean hexadecimal = !suffix.isEmpty();
            for (int index = 0; index < suffix.length(); ++index) {
                if (Character.digit(suffix.charAt(index), 16) < 0) {
                    hexadecimal = false;
                    break;
                }
            }
            if (hexadecimal) return canonical;
        }
        return null;
    }

    private static boolean startsWithIgnoreCase(String value, String prefix) {
        return value.length() >= prefix.length() &&
            value.regionMatches(true, 0, prefix, 0, prefix.length());
    }

    public static final class FunctionFacts {
        final String id;
        final Set<String> symbols = new LinkedHashSet<>();
        final Set<String> strings = new LinkedHashSet<>();
        final Set<Long> scalars = new LinkedHashSet<>();
        final Set<String> calls = new LinkedHashSet<>();

        public FunctionFacts(String id) {
            if (id == null || id.isEmpty()) throw new IllegalArgumentException("id");
            this.id = id;
        }
        public FunctionFacts symbol(String value) {
            String normalized = normalizeSymbol(value);
            if (!normalized.isEmpty()) symbols.add(normalized);
            return this;
        }
        public FunctionFacts stringValue(String value) {
            if (value != null && !value.isEmpty()) strings.add(value);
            return this;
        }
        public FunctionFacts scalar(long value) { scalars.add(value); return this; }
        public FunctionFacts calls(String... ids) {
            for (String value : ids) if (value != null && !value.isEmpty()) calls.add(value);
            return this;
        }
        boolean hasSymbol(String value) {
            String expected = normalizeSymbol(value);
            for (String actual : symbols) if (actual.equalsIgnoreCase(expected)) return true;
            return false;
        }
        boolean hasSymbols(String... values) {
            for (String value : values) if (!hasSymbol(value)) return false;
            return true;
        }
        boolean hasString(String value) {
            String expected = value.toLowerCase(Locale.ROOT);
            for (String actual : strings) {
                if (actual.toLowerCase(Locale.ROOT).contains(expected)) return true;
            }
            return false;
        }
        boolean hasStrings(String... values) {
            for (String value : values) if (!hasString(value)) return false;
            return true;
        }
    }

    public static final class ProgramFacts {
        final Map<String, FunctionFacts> functions = new LinkedHashMap<>();
        final Set<String> imports = new LinkedHashSet<>();
        final Set<String> exports = new LinkedHashSet<>();
        boolean nestedPortableExecutable;
        boolean zipArchive;

        public ProgramFacts function(FunctionFacts value) {
            functions.put(value.id, value); return this;
        }
        public ProgramFacts removeFunction(String id) { functions.remove(id); return this; }
        public ProgramFacts importName(String value) {
            String normalized = normalizeSymbol(value);
            if (!normalized.isEmpty()) imports.add(normalized);
            return this;
        }
        public ProgramFacts exportName(String value) {
            if (value != null && !value.isEmpty()) exports.add(value); return this;
        }
        public ProgramFacts nestedPortableExecutable(boolean value) {
            nestedPortableExecutable = value; return this;
        }
        public ProgramFacts zipArchive(boolean value) { zipArchive = value; return this; }
        boolean hasImport(String value) {
            String expected = normalizeSymbol(value);
            for (String actual : imports) if (actual.equalsIgnoreCase(expected)) return true;
            return false;
        }
        boolean hasImports(String... values) {
            for (String value : values) if (!hasImport(value)) return false;
            return true;
        }
    }

    private static final class Visit {
        final String id; final int depth;
        Visit(String id, int depth) { this.id = id; this.depth = depth; }
    }
}
