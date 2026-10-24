#if !NET
namespace System.Runtime.CompilerServices;

// Enables init accessors on netstandard2.0.
internal static class IsExternalInit
{
}
#endif
