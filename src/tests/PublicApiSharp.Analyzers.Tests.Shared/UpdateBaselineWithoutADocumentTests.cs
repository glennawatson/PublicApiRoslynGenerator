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
}
