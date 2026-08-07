import java.util.LinkedHashMap;
import java.util.List;
import java.util.Set;

final class BlindSoldierVersionEvidenceRulesTests {
    private static final String[] CLAIMS = {
        "SystemVersionLoaderCluster",
        "VersionCacheValidationCluster",
        "AppLoaderSignatureCluster",
        "AppLoaderMarkerParser",
        "AppLoaderTimeoutStateCluster",
        "SupportedHostNameValidation",
        "PackageRootBoundaryValidation",
        "VersionWorkerAndPortableBrokerPrimitives",
        "NoWinmmForwardingSurface",
        "NoEmbeddedExternalRuntime"
    };
    private static final Set<String> SYMBOL_TOKENS = Set.of(
        "GetSystemDirectoryW", "LoadLibraryW", "LoadLibraryExW",
        "GetFileInformationByHandleEx", "CopyFileW", "MoveFileExW",
        "DeleteFileW", "BCryptCreateHash", "BCryptHashData",
        "BCryptFinishHash", "GetFileAttributesW", "GetModuleHandleW",
        "CreateProcessW", "CreateThread");

    public static void main(String[] args) {
        completeRelationalFixturePasses();
        unrelatedGlobalTokensDoNotSatisfyLoaderClaim();
        diagnosticStringsDoNotSatisfySymbolClaims();
        importedPointerAliasesRemainTyped();
        cacheClaimRequiresEveryValidationAndPublicationPrimitive();
        parserMarkersMustShareOneFunction();
        timeoutScalarMustBelongToTheReadinessStateCluster();
        workerEvidenceMustBeConnected();
        brokerDiagnosticsDoNotReplaceTypedProcessSymbol();
        forbiddenImportsAreExactAndComprehensive();
        System.out.println("Blind Soldier Version evidence rule tests passed.");
    }

    private static void completeRelationalFixturePasses() {
        LinkedHashMap<String, Boolean> claims =
            BlindSoldierVersionEvidenceRules.evaluate(baseline(null, true));
        for (String claim : CLAIMS) {
            require(Boolean.TRUE.equals(claims.get(claim)),
                "complete relational fixture failed " + claim);
        }
        require(BlindSoldierVersionEvidenceRules.forbiddenImports(
            baseline(null, true)).isEmpty(),
            "clean Version fixture reported a forbidden import");
    }

    private static void unrelatedGlobalTokensDoNotSatisfyLoaderClaim() {
        BlindSoldierVersionEvidenceRules.ProgramFacts facts = baseline(null, true);
        facts.removeFunction("system-loader");
        facts.function(function("system-directory", "GetSystemDirectoryW"));
        facts.function(function("system-name", "version.dll"));
        facts.function(function("system-load", "LoadLibraryW"));
        facts.function(function("system-load-ex", "LoadLibraryExW"));
        require(!BlindSoldierVersionEvidenceRules.evaluate(facts)
                .get("SystemVersionLoaderCluster"),
            "unrelated global loader tokens satisfied a relational claim");
    }

    private static void diagnosticStringsDoNotSatisfySymbolClaims() {
        BlindSoldierVersionEvidenceRules.ProgramFacts facts = baseline(null, true);
        facts.removeFunction("system-loader");
        BlindSoldierVersionEvidenceRules.FunctionFacts diagnostic =
            new BlindSoldierVersionEvidenceRules.FunctionFacts("diagnostic-loader");
        diagnostic.stringValue("GetSystemDirectoryW")
            .stringValue("LoadLibraryW")
            .stringValue("LoadLibraryExW")
            .stringValue("version.dll");
        facts.function(diagnostic);
        require(!BlindSoldierVersionEvidenceRules.evaluate(facts)
                .get("SystemVersionLoaderCluster"),
            "diagnostic strings falsely satisfied imported-symbol evidence");
    }

    private static void importedPointerAliasesRemainTyped() {
        Set<String> imports = Set.of("CreateProcessW");
        require("CreateProcessW".equals(
                BlindSoldierVersionEvidenceRules.matchImportedSymbol(
                    "PTR_CreateProcessW_1003f0d8", imports)),
            "a Ghidra PTR import alias was not normalized");
        require("CreateProcessW".equals(
                BlindSoldierVersionEvidenceRules.matchImportedSymbol(
                    "__imp__CreateProcessW@40", imports)),
            "a decorated x86 import alias was not normalized");
        require(BlindSoldierVersionEvidenceRules.matchImportedSymbol(
                "CreateProcessW(x86 accessibility broker)", imports) == null,
            "a diagnostic string was accepted as a typed import symbol");
    }

    private static void cacheClaimRequiresEveryValidationAndPublicationPrimitive() {
        String[] required = {
            "GetFileInformationByHandleEx", "CopyFileW", "MoveFileExW",
            "DeleteFileW", "BCryptCreateHash", "BCryptHashData",
            "BCryptFinishHash", "SCALAR:332", "SCALAR:8192",
            "SCALAR:8"
        };
        for (String omitted : required) {
            LinkedHashMap<String, Boolean> claims =
                BlindSoldierVersionEvidenceRules.evaluate(
                    baseline(omitted, true));
            require(!claims.get("VersionCacheValidationCluster"),
                "cache claim accepted missing primitive " + omitted);
        }
    }

    private static void parserMarkersMustShareOneFunction() {
        BlindSoldierVersionEvidenceRules.ProgramFacts facts = baseline(null, true);
        facts.removeFunction("app-loader-parser");
        facts.function(function("init-parser", "AppLoader init log"));
        facts.function(function("success-parser",
            "AppLoader started successfully"));
        require(!BlindSoldierVersionEvidenceRules.evaluate(facts)
                .get("AppLoaderMarkerParser"),
            "separate functions falsely proved one AppLoader parser");
    }

    private static void timeoutScalarMustBelongToTheReadinessStateCluster() {
        LinkedHashMap<String, Boolean> claims =
            BlindSoldierVersionEvidenceRules.evaluate(baseline(null, false));
        require(!claims.get("AppLoaderTimeoutStateCluster"),
            "an unrelated 120000 scalar satisfied the readiness timeout claim");
    }


    private static void workerEvidenceMustBeConnected() {
        BlindSoldierVersionEvidenceRules.ProgramFacts facts =
            baseline(null, true);
        facts.removeFunction("broker-root");
        facts.removeFunction("broker-launch");
        facts.function(function("broker-path",
            "Blind-Soldier-Bootstrap-x86.exe"));
        facts.function(function("broker-ready",
            "Local\\BlindSoldier.Ready."));
        facts.function(function("broker-process", "CreateProcessW"));
        require(!BlindSoldierVersionEvidenceRules.evaluate(facts)
                .get("VersionWorkerAndPortableBrokerPrimitives"),
            "disconnected broker, ready, and CreateProcess facts satisfied " +
                "the worker claim");
    }

    private static void brokerDiagnosticsDoNotReplaceTypedProcessSymbol() {
        BlindSoldierVersionEvidenceRules.ProgramFacts facts =
            baseline(null, true);
        facts.removeFunction("broker-root");
        facts.removeFunction("broker-launch");
        facts.function(function("broker-root",
            "Blind-Soldier-Bootstrap-x86.exe",
            "Local\\BlindSoldier.Ready.").calls(
                "broker-launch-diagnostics", "broker-launch-symbol"));
        facts.function(function("broker-launch-diagnostics",
            "CreateProcessW(x86 accessibility broker)",
            "x86 broker started:"));
        facts.function(function("broker-launch-symbol", "CreateProcessW"));
        require(!BlindSoldierVersionEvidenceRules.evaluate(facts)
                .get("VersionWorkerAndPortableBrokerPrimitives"),
            "broker diagnostics in a different function replaced the typed " +
                "CreateProcessW symbol");
    }

    private static void forbiddenImportsAreExactAndComprehensive() {
        String[] forbidden = {
            "RegCreateKeyW", "RegCreateKeyExW", "RegSetValueExW",
            "RegDeleteKeyExW", "RegDeleteValueW", "RegRenameKey",
            "RegReplaceKeyW", "RegRestoreKeyW", "RegLoadKeyW",
            "VirtualAllocEx", "VirtualProtectEx", "WriteProcessMemory",
            "CreateRemoteThread", "CreateRemoteThreadEx", "NtCreateThreadEx",
            "QueueUserAPC", "SetThreadContext", "RtlCreateUserThread",
            "OpenProcess"
        };
        for (String name : forbidden) {
            BlindSoldierVersionEvidenceRules.ProgramFacts facts =
                baseline(null, true).importName(name);
            List<String> found =
                BlindSoldierVersionEvidenceRules.forbiddenImports(facts);
            require(found.contains(name),
                "forbidden Version import was not rejected: " + name);
        }
        BlindSoldierVersionEvidenceRules.ProgramFacts diagnostic =
            baseline(null, true);
        diagnostic.function(function("diagnostic-only", "RegDeleteValueW"));
        require(BlindSoldierVersionEvidenceRules.forbiddenImports(
                diagnostic).isEmpty(),
            "a non-import diagnostic string triggered the import denylist");
    }

    private static BlindSoldierVersionEvidenceRules.ProgramFacts baseline(
            String omitted, boolean relatedTimeout) {
        BlindSoldierVersionEvidenceRules.ProgramFacts facts =
            new BlindSoldierVersionEvidenceRules.ProgramFacts();
        facts.function(function("system-loader", "GetSystemDirectoryW",
            "version.dll", "LoadLibraryW", "LoadLibraryExW"));
        facts.function(function("cache-root", "version-system-x86-",
            "NativeCache").calls("cache-validate", "cache-copy",
                "cache-publish", "cache-cleanup", "cache-hash"));
        BlindSoldierVersionEvidenceRules.FunctionFacts cacheValidate =
            function("cache-validate",
                maybe("GetFileInformationByHandleEx", omitted));
        addScalarUnless(cacheValidate, 332, omitted);
        addScalarUnless(cacheValidate, 8192, omitted);
        facts.function(cacheValidate);
        facts.function(function("cache-copy", maybe("CopyFileW", omitted)));
        BlindSoldierVersionEvidenceRules.FunctionFacts cachePublish =
            function("cache-publish", maybe("MoveFileExW", omitted));
        addScalarUnless(cachePublish, 8, omitted);
        facts.function(cachePublish);
        facts.function(function("cache-cleanup", maybe("DeleteFileW", omitted)));
        facts.function(function("cache-hash", maybe("BCryptCreateHash", omitted),
            maybe("BCryptHashData", omitted), maybe("BCryptFinishHash", omitted)));
        facts.function(function("stock-signature", "dinput.dll",
            "AppProxy.runtimeconfig.json", "AppProxy.dll", "AppWrapper.dll",
            "nethost.dll").calls("ordinary-file", "loaded-module"));
        facts.function(function("ordinary-file", "GetFileAttributesW"));
        facts.function(function("loaded-module", "GetModuleHandleW"));
        facts.function(function("app-loader-parser", "AppLoader init log",
            "AppLoader started successfully"));
        facts.function(function("readiness", "AppLoader readiness state=")
            .calls("readiness-observe"));
        facts.function(function("readiness-observe",
            "Timed out waiting for AppLoader readiness.",
            "waiting-for-success", "ready-seventh-heaven"));
        if (relatedTimeout) {
            facts.function(function("timeout-caller").scalar(120000)
                .calls("readiness"));
        } else {
            facts.function(function("timeout-unrelated").scalar(120000));
        }
        facts.function(function("supported-host", "ff7_en.exe", "ff7.exe",
            "Legacy host must be named ff7_en.exe or ff7.exe."));
        facts.function(function("root-boundary",
            "within four parent directories", "More than one complete",
            "outside the discovered package root"));
        facts.function(function("broker-root", "Blind-Soldier-Bootstrap-x86.exe",
            "Local\\BlindSoldier.Ready.").calls("broker-launch"));
        facts.function(function("broker-launch", "CreateProcessW",
            "CreateProcessW(x86 accessibility broker)",
            "x86 broker started:"));
        facts.function(function("version-worker", "CreateThread"));
        facts.importName("CreateThread").importName("CreateProcessW");
        return facts;
    }

    private static BlindSoldierVersionEvidenceRules.FunctionFacts function(
            String id, String... tokens) {
        BlindSoldierVersionEvidenceRules.FunctionFacts facts =
            new BlindSoldierVersionEvidenceRules.FunctionFacts(id);
        for (String token : tokens) {
            if (token == null) continue;
            if (SYMBOL_TOKENS.contains(token)) facts.symbol(token);
            else facts.stringValue(token);
        }
        return facts;
    }

    private static void addScalarUnless(
            BlindSoldierVersionEvidenceRules.FunctionFacts facts,
            long value, String omitted) {
        if (!("SCALAR:" + value).equals(omitted)) facts.scalar(value);
    }

    private static String maybe(String value, String omitted) {
        return value.equals(omitted) ? null : value;
    }

    private static void require(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
