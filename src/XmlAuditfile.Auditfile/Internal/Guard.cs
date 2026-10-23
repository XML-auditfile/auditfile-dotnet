namespace XmlAuditfile.Auditfile.Internal;

internal static class Guard
{
    public static T NotNull<T>(T? value, string name)
        where T : class
        => value ?? throw new ArgumentNullException(name);
}
