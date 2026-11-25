using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

// Exercise the actual redistributed loader without installing native hooks.
// An uninitialized instance represents callbacks arriving during its constructor.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("Supply the x86 loader DLL and one or more dependency directories.");
        var directories = args.Skip(1).Prepend(Path.GetDirectoryName(Path.GetFullPath(args[0]))!).ToArray();
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            foreach (string directory in directories)
            {
                string candidate = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(candidate))
                    return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(candidate));
            }
            return null;
        };
        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
        Type injector = assembly.GetType("Reloaded.Mod.Loader.Utilities.DelayInjector", true)!;
        try
        {
            using (var fixture = new CallbackFixture(injector))
            {
                fixture.GrowListWhenDisabled = true;
                fixture.Callback();
                Require(fixture.Disabled == 0 && fixture.Actions == 0,
                    "A callback during construction must leave the growing hook list and action untouched.");
                Console.WriteLine("PASS: callbacks during construction do not enumerate or clear growing lists.");
            }
            using (var fixture = new CallbackFixture(injector))
            {
                fixture.CompleteConstruction();
                Parallel.For(0, 512, _ => fixture.Callback());
                fixture.Callback();
                Require(fixture.Disabled == 1 && fixture.Actions == 1,
                    $"Concurrent/reentrant callbacks must disable once and invoke once; got {fixture.Disabled}/{fixture.Actions}.");
                Console.WriteLine("PASS: 513 callbacks initialize once, including reentry during cleanup.");
            }
            foreach (var (dll, function) in new[] { (-1, 0), (1, 0), (0, -1), (0, int.MaxValue) })
            {
                using var fixture = new CallbackFixture(injector);
                fixture.CompleteConstruction();
                fixture.SetEntryOrdinals(dll, function);
                fixture.Callback();
                Require(fixture.Disabled == 1 && fixture.Actions == 1,
                    "A raced diagnostic entry must not prevent initialization.");
            }
            Console.WriteLine("PASS: invalid or mismatched diagnostic ordinals never prevent initialization.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error is TargetInvocationException { InnerException: not null } wrapped
                ? wrapped.InnerException : error);
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CallbackFixture : IDisposable
    {
        private readonly Type _type;
        private readonly object _instance;
        private readonly MethodInfo _callback;
        private readonly IList _hooks;
        private readonly object _hook;
        private readonly object _logger;
        private readonly nint _entry;
        private readonly nint _function;
        private int _reentered;
        public int Disabled;
        public int Actions;
        public bool GrowListWhenDisabled;

        public CallbackFixture(Type type)
        {
            _type = type;
            _instance = RuntimeHelpers.GetUninitializedObject(type);
            _callback = type.GetMethod("HookImpl", BindingFlags.NonPublic | BindingFlags.Instance)!;
            _hooks = (IList)Activator.CreateInstance(Field("_hooks").FieldType)!;
            Type hookInterface = _hooks.GetType().GenericTypeArguments[0];
            _hook = DispatchProxy.Create(hookInterface, typeof(HookProxy));
            ((HookProxy)_hook).Called = () =>
            {
                Interlocked.Increment(ref Disabled);
                if (GrowListWhenDisabled) _hooks.Add(_hook);
                else if (Interlocked.Exchange(ref _reentered, 1) == 0) Callback();
            };
            _hooks.Add(_hook);
            Set("_hooks", _hooks);
            Set("_wrappers", Activator.CreateInstance(Field("_wrappers").FieldType)!);
            Set("_method", (Action)(() => Interlocked.Increment(ref Actions)));

            var entries = (IList)Activator.CreateInstance(Field("_dlls").FieldType)!;
            Type entryType = entries.GetType().GenericTypeArguments[0];
            object entry = Activator.CreateInstance(entryType)!;
            entryType.GetProperty("Name")!.SetValue(entry, "regression");
            entryType.GetProperty("Functions")!.SetValue(entry, new[] { "callback" });
            entries.Add(entry);
            Set("_dlls", entries);
            _entry = Marshal.AllocHGlobal(sizeof(int));
            _function = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(_entry, 0);
            Marshal.WriteInt32(_function, 0);
            unsafe
            {
                Set("_asmEntryDllOrdinal", Pointer.Box((void*)_entry, Field("_asmEntryDllOrdinal").FieldType));
                Set("_asmEntryFunctionOrdinal", Pointer.Box((void*)_function, Field("_asmEntryFunctionOrdinal").FieldType));
            }
            _logger = Activator.CreateInstance(Field("_logger").FieldType)!;
            Set("_logger", _logger);
        }

        private FieldInfo Field(string name) => _type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(_type.FullName, name);
        private void Set(string name, object value) => Field(name).SetValue(_instance, value);
        public void CompleteConstruction() => Set("_constructionComplete", 1);
        public void SetEntryOrdinals(int dll, int function)
        {
            Marshal.WriteInt32(_entry, dll);
            Marshal.WriteInt32(_function, function);
        }
        public void Callback() => _callback.Invoke(_instance, null);
        public void Dispose()
        {
            _logger.GetType().GetMethod("Shutdown")!.Invoke(_logger, null);
            Marshal.FreeHGlobal(_entry);
            Marshal.FreeHGlobal(_function);
        }
    }
}

public class HookProxy : DispatchProxy
{
    public Action? Called;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "Disable") Called?.Invoke();
        return targetMethod?.ReturnType.IsInstanceOfType(this) == true ? this : null;
    }
}
