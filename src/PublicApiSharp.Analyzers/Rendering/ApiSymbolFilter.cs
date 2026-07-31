// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers;

/// <summary>Decides what counts as public API surface.</summary>
internal static class ApiSymbolFilter
{
    /// <summary>Determines whether a symbol is visible to a consumer of the assembly.</summary>
    /// <remarks>
    /// <c>protected</c> counts: a consumer can derive from a public unsealed type and reach it, so
    /// changing a protected member breaks them exactly as a public one would. <c>internal</c> does
    /// not, and neither does <c>protected internal</c>'s internal half — what survives outside the
    /// assembly is its protected half, which is already covered.
    /// </remarks>
    /// <param name="symbol">The symbol.</param>
    /// <returns><see langword="true"/> when the symbol forms part of the externally visible surface.</returns>
    internal static bool IsExternallyVisible(ISymbol symbol)
    {
        // C# models an explicit interface implementation as private, but a consumer reaches it by
        // casting to the interface, so it is surface whenever its containing type is. Start the walk
        // at the container to skip the member's own misleading accessibility.
        var current = ApiModifiers.IsExplicitInterfaceImplementation(symbol) ? symbol.ContainingSymbol : symbol;
        while (current is not null && current.Kind != SymbolKind.Namespace)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected
                or Accessibility.ProtectedOrInternal))
            {
                return false;
            }

            current = current.ContainingSymbol;
        }

        return true;
    }

    /// <summary>Determines whether a member should be rendered, before accessibility is considered.</summary>
    /// <remarks>
    /// <para>
    /// Compiler-synthesized members are dropped, with one deliberate exception: an implicit
    /// parameterless constructor is real, callable API — a consumer writing <c>new Foo()</c> binds
    /// to it — so it is rendered even though the source never spells it out. Adding a constructor
    /// with parameters silently removes it, which is a genuine breaking change worth surfacing.
    /// </para>
    /// <para>
    /// Everything else the compiler synthesizes is derivable from a declaration that is itself
    /// rendered: a record's <c>Equals</c>/<c>GetHashCode</c>/<c>Deconstruct</c>/<c>&lt;Clone&gt;$</c>
    /// set follows from the record header and its parameters, an enum's constructor from the enum.
    /// Rendering them would add noise that can never change independently, and <c>&lt;Clone&gt;$</c>
    /// is not even expressible in C#, so the baseline could not be parsed back.
    /// </para>
    /// </remarks>
    /// <param name="member">The member.</param>
    /// <returns><see langword="true"/> when the member should appear in the surface.</returns>
    internal static bool IsRenderableMember(ISymbol member)
    {
        // Accessors are rendered as part of their property or event.
        return member is not IMethodSymbol
            {
                MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd
                    or MethodKind.EventRemove or MethodKind.EventRaise,
            }
            && (!member.IsImplicitlyDeclared
            || (member is IMethodSymbol
                {
                    MethodKind: MethodKind.Constructor,
                    Parameters.IsEmpty: true,
                    IsStatic: false,
                }
                && member.ContainingType is { TypeKind: TypeKind.Class, IsRecord: false }));
    }
}
