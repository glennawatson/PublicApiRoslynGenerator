// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

using PublicApiSharp.Analyzers.CodeFixes;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Tests the code fix's behaviour when there is nothing for it to write to.</summary>
/// <remarks>
/// The lightbulb is only offered for a diagnostic the analyzer raised, which implies a baseline
/// exists. A fix-all can still be invoked across a solution where some project has none, so the
/// no-document case has to leave that project alone rather than throw.
/// </remarks>
public class UpdateBaselineWithoutADocumentTests
{
    /// <summary>A language the host has no compilation service for.</summary>
    private const string NoCompilationLanguage = "NoCompilation";

    /// <summary>Verifies a project with no baseline document is returned unchanged.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task ProjectWithoutABaselineIsLeftAloneAsync()
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("NoBaseline", LanguageNames.CSharp);
        _ = workspace.AddDocument(project.Id, "Thing.cs", SourceText.From("public class Thing { }"));

        var reloaded = workspace.CurrentSolution.GetProject(project.Id)!;
        var result = await UpdatePublicApiBaselineCodeFixProvider.UpdateBaselineAsync(reloaded, CancellationToken.None);

        await Assert.That(result).IsEqualTo(reloaded.Solution);
    }

    /// <summary>Verifies a project that cannot produce a compilation is returned unchanged.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// The fix is registered for C#, which always compiles, so only a fix-all reaching across into a
    /// project of another language can land here. There is nothing to render without a compilation,
    /// so that project keeps whatever baseline it already had.
    /// </remarks>
    [Test]
    public async Task ProjectThatCannotCompileIsLeftAloneAsync()
    {
        using var workspace = new AdhocWorkspace();

        var info = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            "NotCompilable",
            "NotCompilable",
            NoCompilationLanguage);

        Project project;
        try
        {
            project = workspace.AddProject(info);
        }
        catch (NotSupportedException)
        {
            // Older workspace hosts refuse a language they hold no services for outright, leaving no
            // project to put the guard to. It is exercised on the hosts that will model one.
            return;
        }

        await Assert.That(project.SupportsCompilation).IsFalse();

        var result = await UpdatePublicApiBaselineCodeFixProvider.UpdateBaselineAsync(project, CancellationToken.None);

        await Assert.That(result).IsEqualTo(project.Solution);
    }
}
