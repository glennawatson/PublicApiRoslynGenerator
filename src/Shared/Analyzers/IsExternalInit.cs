// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.ComponentModel;

namespace System.Runtime.CompilerServices;

/// <summary>Marks an <c>init</c> accessor, which the compiler requires a definition of.</summary>
/// <remarks>
/// <para>
/// The framework declares this from .NET 5 onward, but these assemblies target netstandard2.0 —
/// which is what lets one build load into every host compiler — and that surface predates it. An
/// <c>init</c> accessor or a positional record therefore needs the type supplied here, or the
/// compiler reports CS0518 and the language feature is simply unavailable.
/// </para>
/// <para>
/// Internal on purpose: it is a compiler contract rather than API, and declaring it public would put
/// it in the surface this package exists to record. Two assemblies each compiling their own copy is
/// how the pattern is meant to work — the compiler matches the type by name, not by identity.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit
{
    /// <summary>The first target framework whose own reference assemblies declare this type.</summary>
    /// <remarks>
    /// The condition for deleting this file: once nothing here targets a framework older than this,
    /// the runtime supplies the type and a second declaration would collide with it.
    /// </remarks>
    internal const string DeclaredByFrameworkFrom = "net5.0";
}
