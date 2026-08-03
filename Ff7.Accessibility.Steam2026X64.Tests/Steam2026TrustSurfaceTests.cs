using System.Reflection;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Menus;
using Ff7.Accessibility.Steam2026X64.Runtime.Movies;
using Ff7.Accessibility.Steam2026X64.Runtime.Saves;

internal static class Steam2026TrustSurfaceTests
{
    public static void Run(Steam2026FingerprintResult supported)
    {
        RawTranslatedAndMenuCallbackTypesAreNotExported();
        PublicPointerBearingConstructorsAreFingerprintGated();
        PublicMenuObservationsArePointerFree();
        NativeMovieIdentityAndMetadataAreAddressOpaque();
        SaveSnapshotExposesOnlyNormalizedOccupancy();
        BackendReadFrameFailsClosed(supported);
        TranslatedReadClearsDestinationBeforeAllValidation();
    }

    private static void RawTranslatedAndMenuCallbackTypesAreNotExported()
    {
        var assembly = typeof(Steam2026X64RuntimeBackend).Assembly;
        var exportedNames = assembly.GetExportedTypes()
            .Select(type => type.FullName ?? type.Name)
            .ToHashSet(StringComparer.Ordinal);
        var forbidden = new[]
        {
            typeof(TranslatedX86AddressSpace).FullName!,
            typeof(TranslatedX86CallFrameReader).FullName!,
            typeof(TranslatedFunctionMapValidator).FullName!,
            typeof(TranslatedFunctionMapDefinition).FullName!,
            typeof(Steam2026MenuCallbackCatalog).FullName!,
            typeof(Steam2026MenuCallbackKind).FullName!,
            typeof(TranslatedMenuHostAbi).FullName!,
            typeof(Steam2026MenuCallbackMetadata).FullName!,
            typeof(Steam2026MenuCallbackIdentity).FullName!,
            typeof(TranslatedMenuCursorObservation).FullName!,
            typeof(TranslatedMenuWidgetObservation).FullName!,
            typeof(TranslatedMenuTextObservation).FullName!
        };

        foreach (var typeName in forbidden)
        {
            Equal(false, exportedNames.Contains(typeName), $"raw x64 type {typeName} is internal");
        }
    }

    private static void PublicPointerBearingConstructorsAreFingerprintGated()
    {
        var assembly = typeof(Steam2026X64RuntimeBackend).Assembly;
        foreach (var constructor in assembly.GetExportedTypes()
                     .SelectMany(type => type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)))
        {
            var parameters = constructor.GetParameters();
            var pointerIndex = Array.FindIndex(parameters, parameter =>
                parameter.ParameterType == typeof(ulong)
                && (string.Equals(parameter.Name, "moduleBase", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parameter.Name, "hostAddress", StringComparison.OrdinalIgnoreCase)));
            if (pointerIndex < 0)
            {
                continue;
            }

            Equal(
                true,
                parameters.Take(pointerIndex).Any(parameter =>
                    parameter.ParameterType == typeof(Steam2026FingerprintResult)),
                $"public pointer-bearing constructor {constructor.DeclaringType?.FullName} is fingerprint gated");
        }
    }

    private static void PublicMenuObservationsArePointerFree()
    {
        var activeMethod = typeof(Steam2026MenuObservationReader).GetMethod(
            nameof(Steam2026MenuObservationReader.TryReadActiveWidget));
        Equal(false, activeMethod is null, "public active-widget observation method");
        var activeType = activeMethod!.GetParameters()[1].ParameterType.GetElementType();
        Equal(false, activeType is null, "public active-widget observation type");
        Equal(false, activeType == typeof(ActiveMenuWidgetSnapshot), "public active-widget output is normalized");
        AssertNoPointerProperties(activeType!, "public active-widget observation");

        var magicWidgetProperty = typeof(MagicMenuObservationSnapshot).GetProperty(
            nameof(MagicMenuObservationSnapshot.Widget));
        Equal(false, magicWidgetProperty is null, "public magic widget observation property");
        Equal(false, magicWidgetProperty!.PropertyType == typeof(ActiveMenuWidgetSnapshot), "public magic widget output is normalized");
        AssertNoPointerProperties(magicWidgetProperty.PropertyType, "public magic widget observation");

        var exportedPointerProperties = typeof(Steam2026X64RuntimeBackend).Assembly
            .GetExportedTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(IsPointerProperty)
            .Select(property => $"{property.DeclaringType?.FullName}.{property.Name}")
            .ToArray();
        SequenceEqual(Array.Empty<string>(), exportedPointerProperties, "exported x64 pointer properties");
    }

    private static void NativeMovieIdentityAndMetadataAreAddressOpaque()
    {
        Equal(
            true,
            typeof(NativeMovieCallbackIdentity).GetProperty(
                "Address",
                BindingFlags.Public | BindingFlags.Instance) is null,
            "native movie identity host address is opaque");
        Equal(
            true,
            typeof(NativeMovieCallbackMetadata).GetProperty(
                "Rva",
                BindingFlags.Public | BindingFlags.Instance) is null,
            "native movie public metadata does not expose a reconstructable RVA");
        var publicRvaFields = typeof(NativeMovieCallbackContract)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Where(IsPointerField)
            .Select(field => field.Name)
            .ToArray();
        SequenceEqual(Array.Empty<string>(), publicRvaFields, "native movie contract exposes no public address constants");
    }

    private static void SaveSnapshotExposesOnlyNormalizedOccupancy()
    {
        var properties = typeof(Steam2026SaveContainerContractSnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();
        Equal(false, properties.Contains("RawSelectionByte", StringComparer.Ordinal), "save snapshot hides packed selection byte");
        Equal(false, properties.Contains("OccupancyMask", StringComparer.Ordinal), "save snapshot hides raw occupancy mask");
        Equal(true, properties.Contains("VerifiedOccupiedSlotIndices", StringComparer.Ordinal), "save snapshot exposes verified occupied slots");
        Equal(true, properties.Contains("StaticAutosaveSlotIsOccupied", StringComparer.Ordinal), "save snapshot exposes static autosave occupancy");
        Equal(2, properties.Length, "save snapshot exposes only normalized occupancy evidence");
    }

    private static void BackendReadFrameFailsClosed(Steam2026FingerprintResult supported)
    {
        using var backend = new Steam2026X64RuntimeBackend(supported);
        Equal(
            true,
            Throws<InvalidOperationException>(() => _ = backend.ReadFrame()),
            "research-only backend refuses frame publication");
    }

    private static void TranslatedReadClearsDestinationBeforeAllValidation()
    {
        const ulong moduleBase = 0x0000000140000000;
        const ulong hostPage = 0x0000000200100000;
        var memory = new FakeNativeMemoryReader();
        memory.MapVirtualPage(moduleBase, uint.MaxValue >> 12, hostPage);
        var addressSpace = new TranslatedX86AddressSpace(moduleBase, memory);

        Span<byte> nullDestination = stackalloc byte[4];
        nullDestination.Fill(0xA5);
        Equal(false, addressSpace.TryRead(0, nullDestination), "null translated address rejected");
        Equal("00000000", Convert.ToHexString(nullDestination), "null translated address clears stale destination");

        Span<byte> wrappingDestination = stackalloc byte[4];
        wrappingDestination.Fill(0x5A);
        Equal(false, addressSpace.TryRead(0xFFFFFFFE, wrappingDestination), "wrapping translated range rejected");
        Equal("00000000", Convert.ToHexString(wrappingDestination), "wrapping translated range clears stale destination");
    }

    private static void AssertNoPointerProperties(Type type, string label)
    {
        var pointerProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(IsPointerProperty)
            .Select(property => property.Name)
            .ToArray();
        SequenceEqual(Array.Empty<string>(), pointerProperties, label);
    }

    private static bool IsPointerProperty(PropertyInfo property) =>
        IsPointerMember(property.Name, property.PropertyType);

    private static bool IsPointerField(FieldInfo field) =>
        IsPointerMember(field.Name, field.FieldType);

    private static bool IsPointerMember(string name, Type type) =>
        (name is "Address" or "HostAddress" or "GuestWidgetAddress"
            || name.Contains("Pointer", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Rva", StringComparison.OrdinalIgnoreCase))
        && (type == typeof(uint)
            || type == typeof(ulong)
            || type == typeof(nuint)
            || type == typeof(IntPtr)
            || type == typeof(UIntPtr));

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string label)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(',', expected)}], got [{string.Join(',', actual)}].");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
