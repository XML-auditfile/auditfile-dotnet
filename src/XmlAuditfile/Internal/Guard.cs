namespace XmlAuditfile.Internal;

internal static class Guard
{
    public static T NotNull<T>(T? value, string name)
        where T : class
        => value ?? throw new ArgumentNullException(name);

    public static T AtLeast<T>(T value, T minimum, string name)
        where T : IComparable<T>
        => value.CompareTo(minimum) >= 0 ? value : throw new ArgumentOutOfRangeException(name, value, $"{name} must be at least {minimum}.");
}
