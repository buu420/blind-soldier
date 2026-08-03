using System.Text.Json;
using Ff7.Accessibility.Runtime.Abstractions;

const string LegacySha256 = "4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225";
const string Steam2026Sha256 = "57A23D166D69E46B9E3339F779D4A3C4FEB402A989FA7291D0D9B4A1953ABB4B";

var matrixPath = Path.Combine(AppContext.BaseDirectory, "parity-matrix.json");
using var document = JsonDocument.Parse(File.ReadAllText(matrixPath));
var root = document.RootElement;

AssertEqual(1, root.GetProperty("schemaVersion").GetInt32(), "schema version");

var policy = root.GetProperty("policy");
AssertEqual("FullParity", policy.GetProperty("requiredCapabilityMask").GetString(), "required mask");
AssertEqual(false, policy.GetProperty("partialRuntimeMayBeReleased").GetBoolean(), "partial release policy");
AssertEqual(false, policy.GetProperty("staticEvidenceMayEnableSpeech").GetBoolean(), "static evidence policy");
AssertEqual(true, policy.GetProperty("falseOrIncompleteSpeechIsFailure").GetBoolean(), "false speech policy");

var requiredCapabilities = Enum.GetValues<RuntimeCapability>()
    .Where(IsSingleBitCapability)
    .Select(capability => capability.ToString())
    .OrderBy(name => name, StringComparer.Ordinal)
    .ToArray();
AssertEqual(9, requiredCapabilities.Length, "required capability count");

var capabilityEntries = root.GetProperty("capabilities").EnumerateArray().ToArray();
var matrixCapabilities = capabilityEntries
    .Select(entry => RequiredString(entry, "capability"))
    .OrderBy(name => name, StringComparer.Ordinal)
    .ToArray();
AssertSequenceEqual(requiredCapabilities, matrixCapabilities, "matrix capability coverage");
AssertEqual(matrixCapabilities.Length, matrixCapabilities.Distinct(StringComparer.Ordinal).Count(), "unique capabilities");

foreach (var entry in capabilityEntries)
{
    var capability = RequiredString(entry, "capability");
    AssertNotBlank(RequiredString(entry, "legacyX86"), $"{capability} legacy status");
    AssertNotBlank(RequiredString(entry, "steam2026X64"), $"{capability} x64 status");
    AssertNonEmptyArray(entry, "staticEvidence", $"{capability} static evidence");
    AssertNonEmptyArray(entry, "requiredLiveEvidence", $"{capability} live evidence requirements");
}

var runtimes = root.GetProperty("runtimes");
var legacy = runtimes.GetProperty("legacyX86");
AssertEqual("ff7-legacy-x86", RequiredString(legacy, "runtimeId"), "legacy runtime id");
AssertEqual(LegacySha256, RequiredString(legacy, "sha256"), "legacy fingerprint");
var native = runtimes.GetProperty("steam2026X64");
AssertEqual("ff7-steam-2026-x64", RequiredString(native, "runtimeId"), "x64 runtime id");
AssertEqual(Steam2026Sha256, RequiredString(native, "sha256"), "x64 fingerprint");

var releaseGate = root.GetProperty("releaseGate");
AssertEqual(true, releaseGate.GetProperty("legacyX86Ready").GetBoolean(), "legacy release gate");
var x64Ready = releaseGate.GetProperty("steam2026X64Ready").GetBoolean();
AssertEqual(true, releaseGate.GetProperty("requiredUserLedValidation").GetBoolean(), "user-led validation gate");
var blockingCapabilities = releaseGate.GetProperty("blockingCapabilities")
    .EnumerateArray()
    .Select(item => item.GetString() ?? string.Empty)
    .OrderBy(name => name, StringComparer.Ordinal)
    .ToArray();
var enabledX64Capabilities = capabilityEntries
    .Where(entry => entry.GetProperty("x64SpeechEnabled").GetBoolean())
    .Select(entry => RequiredString(entry, "capability"))
    .OrderBy(name => name, StringComparer.Ordinal)
    .ToArray();
if (x64Ready)
{
    AssertSequenceEqual(requiredCapabilities, enabledX64Capabilities, "released x64 capability coverage");
    AssertEqual(0, blockingCapabilities.Length, "released x64 blocking capabilities");
    AssertEqual("supported", RequiredString(native, "releaseStatus"), "released x64 status");
}
else
{
    AssertEqual(0, enabledX64Capabilities.Length, "partial x64 speech prohibition");
    AssertSequenceEqual(requiredCapabilities, blockingCapabilities, "blocking capability coverage");
    AssertEqual("research-only-fail-closed", RequiredString(native, "releaseStatus"), "blocked x64 status");
}

Console.WriteLine("FFVII dual-runtime parity matrix tests passed.");

static bool IsSingleBitCapability(RuntimeCapability capability)
{
    var value = (int)capability;
    return value > 0 && (value & (value - 1)) == 0;
}

static string RequiredString(JsonElement element, string propertyName)
{
    var value = element.GetProperty(propertyName).GetString();
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"{propertyName} must not be blank.");
    }

    return value;
}

static void AssertNonEmptyArray(JsonElement element, string propertyName, string description)
{
    var values = element.GetProperty(propertyName).EnumerateArray().ToArray();
    if (values.Length == 0 || values.Any(value => string.IsNullOrWhiteSpace(value.GetString())))
    {
        throw new InvalidOperationException($"Assertion failed for {description}: expected non-empty strings.");
    }
}

static void AssertNotBlank(string value, string description)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Assertion failed for {description}: value is blank.");
    }
}

static void AssertSequenceEqual(
    IReadOnlyList<string> expected,
    IReadOnlyList<string> actual,
    string description)
{
    if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
    {
        throw new InvalidOperationException(
            $"Assertion failed for {description}: expected [{string.Join(", ", expected)}], " +
            $"actual [{string.Join(", ", actual)}].");
    }
}

static void AssertEqual<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"Assertion failed for {description}: expected {expected}, actual {actual}.");
    }
}
