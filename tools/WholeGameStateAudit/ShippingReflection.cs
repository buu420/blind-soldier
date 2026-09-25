using System.Collections;
using System.Reflection;
using Ff7.Accessibility.Reloaded;

namespace WholeGameStateAudit;

internal sealed record ShippingGroup(int Index, string Name, IReadOnlyDictionary<int, byte[]> Scripts);

internal sealed record ShippingOpcode(byte Id, int Offset, byte[] Bytes);

/// <summary>
/// The shipping catalog's own private decoder, called as it is rather than re-implemented,
/// so the comparison measures the code that ships. Reflection only reads; nothing here
/// changes how the catalog behaves.
/// </summary>
internal static class ShippingReflection
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type Catalog = typeof(FieldScriptNavigationCatalog);

    private static readonly MethodInfo ParseScriptGroupsMethod = Require("ParseScriptGroups");
    private static readonly MethodInfo ReadOpcodesMethod = Require("ReadOpcodes");
    private static readonly MethodInfo CollectTalkDialogIdsMethod = Require("CollectTalkDialogIds");
    private static readonly MethodInfo TryReadSectionOneMethod = Require("TryReadSectionOne");

    /// <summary>
    /// Whether the catalog compiled in is the repaired one, which reads scripts through its
    /// FieldScriptProgram. The 0.6.8 catalog has no such type, and only it is what the
    /// audit's baseline walker replica (<see cref="BaselineShippingWalk"/>) explains.
    /// </summary>
    public static bool IsRepairedCatalog { get; } =
        Catalog.Assembly.GetType("Ff7.Accessibility.Reloaded.FieldScriptProgram") is not null;

    private static readonly MethodInfo? ReadFieldFromBytesMethod = Catalog.GetMethod("ReadFieldFromBytes", PrivateStatic);

    private static MethodInfo Require(string name) =>
        Catalog.GetMethod(name, PrivateStatic)
        ?? throw new MissingMethodException(Catalog.FullName, name);

    public static byte[]? ReadSectionOne(byte[] fieldBytes)
    {
        var arguments = new object?[] { fieldBytes, null, null };
        return (bool)TryReadSectionOneMethod.Invoke(null, arguments)! ? (byte[])arguments[1]! : null;
    }

    /// <summary>The parsed groups, and the catalog's own list object for calls that need it back.</summary>
    public static (IReadOnlyList<ShippingGroup> Groups, object Native) ParseScriptGroups(byte[] section)
    {
        var arguments = new object?[] { section, null };
        var native = ParseScriptGroupsMethod.Invoke(null, arguments)!;
        var groups = new List<ShippingGroup>();
        foreach (var group in (IEnumerable)native)
        {
            var type = group.GetType();
            groups.Add(new ShippingGroup(
                (int)type.GetProperty("Index")!.GetValue(group)!,
                (string)type.GetProperty("Name")!.GetValue(group)!,
                (IReadOnlyDictionary<int, byte[]>)type.GetProperty("Scripts")!.GetValue(group)!));
        }

        return (groups, native);
    }

    public static IReadOnlyList<ShippingOpcode> ReadOpcodes(byte[] script)
    {
        var list = new List<ShippingOpcode>();
        foreach (var opcode in (IEnumerable)ReadOpcodesMethod.Invoke(null, [script])!)
        {
            var type = opcode.GetType();
            list.Add(new ShippingOpcode(
                (byte)type.GetProperty("Id")!.GetValue(opcode)!,
                (int)type.GetProperty("Offset")!.GetValue(opcode)!,
                (byte[])type.GetProperty("Bytes")!.GetValue(opcode)!));
        }

        return list;
    }

    public static IReadOnlyList<int> CollectTalkDialogIds(object nativeGroups, int groupIndex, int scriptIndex)
    {
        var dialogIds = new List<int>();
        CollectTalkDialogIdsMethod.Invoke(null,
            [nativeGroups, groupIndex, scriptIndex, dialogIds, new HashSet<(int Group, int Script)>()]);
        return dialogIds;
    }

    /// <summary>
    /// What the repaired catalog publishes for a field's bytes, through the same method its
    /// public ReadField uses once the archive is read; null for the 0.6.8 catalog.
    /// </summary>
    public static FieldScriptNavigationReadResult? ReadFieldFromBytes(int fieldId, string fieldName, byte[] fieldBytes) =>
        ReadFieldFromBytesMethod?.Invoke(null, [fieldId, fieldName, fieldBytes]) as FieldScriptNavigationReadResult;

    // The entry point ReadField calls: no walk context yet, one is made for the root.
    private static readonly MethodInfo CollectNavigationActionPathsMethod =
        Catalog.GetMethods(PrivateStatic).Single(method =>
            method.Name == "CollectNavigationActionPaths" && method.GetParameters().Length == 5);

    private static readonly Type BankByteAddressType =
        Catalog.GetNestedType("BankByteAddress", BindingFlags.NonPublic)
        ?? throw new MissingMemberException(Catalog.FullName, "BankByteAddress");

    /// <summary>
    /// Every action the shipping walker collects from one script, exactly as ReadFieldCore
    /// asks for them: no initial constants and an empty call stack.
    /// </summary>
    public static IReadOnlyList<ShippingAction> CollectNavigationActions(object nativeGroups, int group, int script) =>
        CollectNavigationPaths(nativeGroups, group, script).SelectMany(path => path).ToArray();

    /// <summary>Each way through the script the shipping walker returns, with its actions in order.</summary>
    public static IReadOnlyList<IReadOnlyList<ShippingAction>> CollectNavigationPaths(object nativeGroups, int group, int script)
    {
        var constants = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(BankByteAddressType, typeof(byte)))!;
        var paths = (IEnumerable)CollectNavigationActionPathsMethod.Invoke(
            null,
            [nativeGroups, group, script, constants, new HashSet<(int Group, int Script)>()])!;
        var result = new List<IReadOnlyList<ShippingAction>>();
        foreach (var path in paths)
        {
            var actions = new List<ShippingAction>();
            result.Add(actions);
            foreach (var action in (IEnumerable)path.GetType().GetProperty("Actions")!.GetValue(path)!)
            {
                var type = action.GetType();
                T Get<T>(string name) => (T)type.GetProperty(name)!.GetValue(action)!;
                actions.Add(new ShippingAction(
                    type.GetProperty("Kind")!.GetValue(action)!.ToString()!,
                    Get<int>("SourceGroup"),
                    Get<int>("SourceScript"),
                    Get<int>("X"),
                    Get<int>("Y"),
                    (int?)type.GetProperty("Z")!.GetValue(action),
                    Get<int>("Triangle"),
                    Get<int>("DestinationField"),
                    Get<bool>("IsPlacement")));
            }
        }

        return result;
    }
}

internal sealed record ShippingAction(
    string Kind,
    int SourceGroup,
    int SourceScript,
    int X,
    int Y,
    int? Z,
    int Triangle,
    int DestinationField,
    bool IsPlacement)
{
    /// <summary>What the action does, in the same terms as an engine navigation instruction.</summary>
    public string Signature => Kind switch
    {
        "MapJump" => $"MapJump {DestinationField}",
        "Ladder" => $"Ladder ({X},{Y},{Z}) {Triangle}",
        "Jump" when IsPlacement => $"Place ({X},{Y},{Z}) {Triangle}",
        "Jump" => $"Jump ({X},{Y}) {Triangle}",
        _ => $"{Kind}?"
    };
}
