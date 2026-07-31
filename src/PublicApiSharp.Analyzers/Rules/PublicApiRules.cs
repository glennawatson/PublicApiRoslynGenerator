// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers;

/// <summary>
/// Diagnostic descriptors for the public API baseline rules (PAS00xx). Every rule here compares
/// the compilation's externally visible surface against the checked-in baseline for the target
/// framework being built.
/// </summary>
internal static class PublicApiRules
{
    /// <summary>The category every rule in this package reports under.</summary>
    internal const string Category = "PublicApi";

    /// <summary>The diagnostic id for a member that is missing from the baseline.</summary>
    internal const string AddedId = "PAS0001";

    /// <summary>The diagnostic id for a baseline entry whose member no longer exists.</summary>
    internal const string RemovedId = "PAS0002";

    /// <summary>The diagnostic id for a member whose declaration differs from the baseline.</summary>
    internal const string ChangedId = "PAS0003";

    /// <summary>The diagnostic id for a target framework that has no baseline file.</summary>
    internal const string MissingBaselineId = "PAS0004";

    /// <summary>The diagnostic id for a baseline file that could not be read.</summary>
    internal const string UnreadableBaselineId = "PAS0005";

    /// <summary>
    /// PAS0001: the compilation exposes something the baseline does not mention. Reported on the
    /// declaration itself so the squiggle lands where the decision was made.
    /// </summary>
    internal static readonly DiagnosticDescriptor Added = DescriptorFactory.CreateError(
        AddedId,
        "Public API is not in the baseline",
        "'{0}' is public API but is not declared in the baseline for this target framework",
        Category,
        "Everything this assembly exposes is recorded in a checked-in baseline file so that a change to "
        + "the surface is a reviewable diff rather than a surprise for consumers. This declaration is "
        + "visible outside the assembly but the baseline does not mention it. Accept it into the "
        + "baseline, or reduce its accessibility.");

    /// <summary>
    /// PAS0002: the baseline mentions something the compilation no longer exposes. Reported on the
    /// baseline line, because that is the text a reviewer has to agree to delete.
    /// </summary>
    internal static readonly DiagnosticDescriptor Removed = DescriptorFactory.CreateError(
        RemovedId,
        "Public API in the baseline no longer exists",
        "'{0}' is declared in the baseline but no longer exists in the public API",
        Category,
        "The baseline records a member that the assembly no longer exposes. Removing public API breaks "
        + "every consumer that used it, so the deletion has to be an explicit, reviewed edit to the "
        + "baseline rather than a silent side effect of a refactor.");

    /// <summary>
    /// PAS0003: same member, different declaration — a changed return type, parameter default,
    /// modifier, nullability or attribute. Reported on the declaration.
    /// </summary>
    internal static readonly DiagnosticDescriptor Changed = DescriptorFactory.CreateError(
        ChangedId,
        "Public API differs from the baseline",
        "'{0}' differs from the baseline; baseline declares '{1}' but the API is '{2}'",
        Category,
        "The member still exists but its declaration no longer matches the baseline. A changed return "
        + "type, parameter default, nullability annotation, modifier or attribute is visible to "
        + "consumers and can break them at compile time or at run time even when the member name and "
        + "parameter types are unchanged.");

    /// <summary>
    /// PAS0004: no baseline file exists for the target framework being built. Opt-in, because
    /// installing the package should not break a build before a baseline has been adopted; enable
    /// it to bootstrap one through the code fix.
    /// </summary>
    internal static readonly DiagnosticDescriptor MissingBaseline = DescriptorFactory.CreateOptIn(
        MissingBaselineId,
        "No public API baseline for this target framework",
        "There is no public API baseline at '{0}' for target framework '{1}'",
        Category,
        "Public API tracking only covers a target framework once a baseline exists for it, so a "
        + "multi-targeting project silently loses coverage for any target whose baseline was never "
        + "created. Enable this rule to be told which targets are untracked; the code fix writes the "
        + "current surface out as the starting baseline.");

    /// <summary>
    /// PAS0005: the baseline exists but could not be turned into a set of declarations. Reported
    /// on the offending line so a hand-edit that broke the file is easy to find.
    /// </summary>
    internal static readonly DiagnosticDescriptor UnreadableBaseline = DescriptorFactory.CreateError(
        UnreadableBaselineId,
        "Public API baseline could not be read",
        "The public API baseline could not be read: {0}",
        Category,
        "The baseline is C# declaration text, so a hand-edit that leaves it unparseable would otherwise "
        + "make every member look removed. This rule reports the malformed content instead, keeping "
        + "the failure pointed at the real cause.");
}
