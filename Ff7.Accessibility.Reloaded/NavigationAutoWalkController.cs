using Ff7.Accessibility.Core;

namespace Ff7.Accessibility.Reloaded;

internal enum NavigationAutoWalkDomain
{
    None,
    Field,
    WorldMap
}

internal static class NavigationAutoWalkKeyRouter
{
    internal const int VirtualKeyP = 0x50;

    internal static bool ObserveToggle(Func<int, bool> observeRisingEdge)
    {
        ArgumentNullException.ThrowIfNull(observeRisingEdge);
        return observeRisingEdge(VirtualKeyP);
    }
}

/// <summary>
/// Owns only the directional arrow keys used by automatic navigation. Route
/// selection, safety, arrival, and speech remain with the field/world adapters.
/// </summary>
internal sealed class NavigationAutoWalkController : IDisposable
{
    private readonly HighwayAutoSteeringController directionalInput;
    private NavigationAutoWalkDomain domain;
    private bool disposed;

    internal NavigationAutoWalkController(IHighwayKeyboardInputSink sink)
        : this(new HighwayAutoSteeringController(sink))
    {
    }

    private NavigationAutoWalkController(HighwayAutoSteeringController directionalInput)
    {
        this.directionalInput = directionalInput ?? throw new ArgumentNullException(nameof(directionalInput));
    }

    internal static NavigationAutoWalkController CreateCurrentProcess() =>
        new(HighwayAutoSteeringController.CreateCurrentProcess());

    internal bool Enabled => !disposed && domain != NavigationAutoWalkDomain.None;

    internal string LastDiagnostic { get; private set; } = string.Empty;

    internal bool IsEnabledFor(NavigationAutoWalkDomain owner) =>
        Enabled && domain == owner;

    internal bool TryStart(NavigationAutoWalkDomain owner, bool routeActive)
    {
        if (disposed || owner == NavigationAutoWalkDomain.None || !routeActive)
        {
            return false;
        }

        if (domain != NavigationAutoWalkDomain.None && domain != owner)
        {
            _ = directionalInput.ReleaseAll();
        }

        domain = owner;
        LastDiagnostic = $"auto walk active for {owner}";
        return true;
    }

    internal HighwayAutoSteeringInputResult Drive(
        FieldNavigationInput input,
        bool canMove,
        bool routeActive)
    {
        if (disposed)
        {
            return directionalInput.Apply(HighwaySteeringDirection.None);
        }

        if (!routeActive)
        {
            var release = directionalInput.ReleaseAll();
            domain = NavigationAutoWalkDomain.None;
            LastDiagnostic = release.Success
                ? "auto walk stopped because navigation is inactive"
                : release.Diagnostic;
            return release;
        }

        if (!Enabled || !canMove || !IsDirectional(input))
        {
            var release = directionalInput.ReleaseAll();
            LastDiagnostic = release.Success
                ? !Enabled
                    ? "auto walk inactive"
                    : "auto walk direction suspended"
                : release.Diagnostic;
            return release;
        }

        var result = directionalInput.Apply(Map(input));
        if (!result.Success)
        {
            _ = directionalInput.ReleaseAll();
            domain = NavigationAutoWalkDomain.None;
            LastDiagnostic = result.Diagnostic;
            return result;
        }

        LastDiagnostic = $"auto walk direction {input}";
        return result;
    }

    internal void Suspend()
    {
        if (disposed)
        {
            return;
        }

        var result = directionalInput.ReleaseAll();
        LastDiagnostic = result.Success
            ? Enabled ? "auto walk suspended" : "auto walk inactive"
            : result.Diagnostic;
    }

    internal bool Stop()
    {
        if (disposed)
        {
            return false;
        }

        var wasEnabled = Enabled;
        var result = directionalInput.ReleaseAll();
        domain = NavigationAutoWalkDomain.None;
        LastDiagnostic = result.Success ? "auto walk stopped" : result.Diagnostic;
        return wasEnabled;
    }

    internal void Reset()
    {
        if (disposed)
        {
            return;
        }

        _ = directionalInput.ReleaseAll();
        domain = NavigationAutoWalkDomain.None;
        LastDiagnostic = "auto walk reset";
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        directionalInput.Dispose();
        domain = NavigationAutoWalkDomain.None;
        disposed = true;
    }

    private static bool IsDirectional(FieldNavigationInput input) =>
        input is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft;

    private static HighwaySteeringDirection Map(FieldNavigationInput input) => input switch
    {
        FieldNavigationInput.Up => HighwaySteeringDirection.Up,
        FieldNavigationInput.UpRight => HighwaySteeringDirection.UpRight,
        FieldNavigationInput.Right => HighwaySteeringDirection.Right,
        FieldNavigationInput.DownRight => HighwaySteeringDirection.DownRight,
        FieldNavigationInput.Down => HighwaySteeringDirection.Down,
        FieldNavigationInput.DownLeft => HighwaySteeringDirection.DownLeft,
        FieldNavigationInput.Left => HighwaySteeringDirection.Left,
        FieldNavigationInput.UpLeft => HighwaySteeringDirection.UpLeft,
        _ => HighwaySteeringDirection.None
    };
}
