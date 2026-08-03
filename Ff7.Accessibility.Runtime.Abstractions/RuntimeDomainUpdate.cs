namespace Ff7.Accessibility.Runtime.Abstractions;

public enum RuntimeDomainUpdateKind
{
    Unchanged,
    Present,
    Closed
}

public readonly record struct RuntimeDomainUpdate<T>
    where T : class
{
    private RuntimeDomainUpdate(RuntimeDomainUpdateKind kind, T? value)
    {
        Kind = kind;
        Value = value;
    }

    public RuntimeDomainUpdateKind Kind { get; }

    public T? Value { get; }

    public static RuntimeDomainUpdate<T> Unchanged { get; } =
        new(RuntimeDomainUpdateKind.Unchanged, null);

    public static RuntimeDomainUpdate<T> Closed { get; } =
        new(RuntimeDomainUpdateKind.Closed, null);

    public static RuntimeDomainUpdate<T> Present(T value) =>
        new(RuntimeDomainUpdateKind.Present, value ?? throw new ArgumentNullException(nameof(value)));
}
