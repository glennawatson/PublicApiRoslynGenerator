// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
using System.Threading;

namespace PublicApiSharp.Analyzers;

/// <summary>Turns API surface text into the flat set of declarations it describes.</summary>
/// <remarks>
/// <para>
/// The baseline format is C# declaration text, which means the compiler can read it back: this
/// parses it with the same Roslyn parser that compiled the project. That is deliberate — the same
/// function is run over the freshly rendered surface <em>and</em> over the checked-in baseline, so
/// the two sides of the comparison cannot drift apart through two subtly different notions of what
/// a declaration is.
/// </para>
/// <para>
/// Parsing uses <see cref="LanguageVersion.Preview"/> so a baseline containing the newest syntax the
/// host Roslyn understands still reads back cleanly.
/// </para>
/// </remarks>
internal static partial class ApiTextParser
{
    /// <summary>The options every parse uses.</summary>
    private static readonly CSharpParseOptions ParseOptions =
        new(LanguageVersion.Preview, DocumentationMode.None, SourceCodeKind.Regular);

    /// <summary>Parses API surface text into its declarations.</summary>
    /// <param name="text">The API surface text.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The declarations, or the syntax error that stopped them being read.</returns>
    internal static ApiTextParseResult Parse(SourceText text, CancellationToken cancellationToken)
    {
        var tree = CSharpSyntaxTree.ParseText(text, ParseOptions, cancellationToken: cancellationToken);

        foreach (var diagnostic in tree.GetDiagnostics(cancellationToken))
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return ApiTextParseResult.Malformed(diagnostic.GetMessage(), diagnostic.Location.SourceSpan);
            }
        }

        var root = (CompilationUnitSyntax)tree.GetRoot(cancellationToken);
        var builder = ImmutableArray.CreateBuilder<ApiDeclaration>();

        foreach (var attributeList in root.AttributeLists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddAssemblyAttributes(attributeList, builder, text);
        }

        VisitMembers(root.Members, string.Empty, builder, text, cancellationToken);
        return ApiTextParseResult.Parsed(builder.ToImmutable());
    }

    /// <summary>Appends one parameter's contribution to an overload identity.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="parameter">The parameter.</param>
    internal static void AppendParameterIdentity(PooledStringBuilder builder, ParameterSyntax parameter)
    {
        foreach (var modifier in parameter.Modifiers)
        {
            if (IsReferenceKind(modifier))
            {
                _ = builder.Append(modifier.ValueText).Append(' ');
            }
        }

        if (parameter.Type is null)
        {
            return;
        }

        _ = builder.Append(RemoveWhitespace(parameter.Type.ToString()));
    }

    /// <summary>Records every attribute of an assembly-level attribute list.</summary>
    /// <param name="attributeList">The attribute list.</param>
    /// <param name="builder">The declaration builder.</param>
    /// <param name="text">The text being parsed.</param>
    private static void AddAssemblyAttributes(
        AttributeListSyntax attributeList,
        ImmutableArray<ApiDeclaration>.Builder builder,
        SourceText text)
    {
        // An assembly attribute has no identity beyond its whole application: the same attribute
        // type can legitimately be applied more than once with different arguments.
        foreach (var attribute in attributeList.Attributes)
        {
            Add(builder, text, $"[assembly]{RemoveWhitespace(attribute.ToString())}", attributeList.Span);
        }
    }

    /// <summary>Walks a list of member declarations.</summary>
    /// <param name="members">The members.</param>
    /// <param name="container">The dotted name of the enclosing namespace and types.</param>
    /// <param name="builder">The declaration builder.</param>
    /// <param name="text">The text being parsed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private static void VisitMembers(
        SyntaxList<MemberDeclarationSyntax> members,
        string container,
        ImmutableArray<ApiDeclaration>.Builder builder,
        SourceText text,
        CancellationToken cancellationToken)
    {
        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VisitMember(member, container, builder, text, cancellationToken);
        }
    }

    /// <summary>Records one member declaration, recursing into namespaces and types.</summary>
    /// <param name="member">The member.</param>
    /// <param name="container">The dotted name of the enclosing namespace and types.</param>
    /// <param name="builder">The declaration builder.</param>
    /// <param name="text">The text being parsed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private static void VisitMember(
        MemberDeclarationSyntax member,
        string container,
        ImmutableArray<ApiDeclaration>.Builder builder,
        SourceText text,
        CancellationToken cancellationToken)
    {
        // An extension block is a TypeDeclarationSyntax, so it has to be recognised before the
        // general type case. Only some slots have the syntax at all; see the tier files.
        if (TryVisitExtensionBlock(member, container, builder, text, cancellationToken))
        {
            return;
        }

        switch (member)
        {
            case BaseNamespaceDeclarationSyntax ns:
            {
                VisitMembers(ns.Members, Combine(container, ns.Name.ToString()), builder, text, cancellationToken);
                break;
            }

            case TypeDeclarationSyntax type:
            {
                // The arity belongs to the container path, not only to the type's own entry: a
                // generic type and a non-generic one of the same name are unrelated, and so is
                // everything they declare. Dropping it here would file a member of Thing<T> under
                // Thing, where a member of Thing with the same name already sits.
                var qualified = TypeIdentity(Combine(container, type.Identifier.ValueText), Arity(type.TypeParameterList));
                Add(builder, text, qualified, HeaderSpan(type, type.OpenBraceToken));
                VisitMembers(type.Members, qualified, builder, text, cancellationToken);
                break;
            }

            case EnumDeclarationSyntax enumType:
            {
                var qualified = Combine(container, enumType.Identifier.ValueText);
                Add(builder, text, TypeIdentity(qualified, 0), HeaderSpan(enumType, enumType.OpenBraceToken));
                foreach (var enumMember in enumType.Members)
                {
                    Add(builder, text, Combine(qualified, enumMember.Identifier.ValueText), enumMember.Span);
                }

                break;
            }

            case DelegateDeclarationSyntax del:
            {
                var qualified = Combine(container, del.Identifier.ValueText);
                Add(builder, text, TypeIdentity(qualified, Arity(del.TypeParameterList)), del.Span);
                break;
            }

            default:
            {
                VisitTypeMember(member, container, builder, text);
                break;
            }
        }
    }

    /// <summary>Records one declaration that is a member of a type rather than a type itself.</summary>
    /// <param name="member">The member.</param>
    /// <param name="container">The dotted name of the enclosing namespace and types.</param>
    /// <param name="builder">The declaration builder.</param>
    /// <param name="text">The text being parsed.</param>
    private static void VisitTypeMember(
        MemberDeclarationSyntax member,
        string container,
        ImmutableArray<ApiDeclaration>.Builder builder,
        SourceText text)
    {
        switch (member)
        {
            case MethodDeclarationSyntax method:
            {
                var name = Combine(container, MemberName(method.ExplicitInterfaceSpecifier, method.Identifier));
                Add(builder, text, $"{name}{ArityMarker(Arity(method.TypeParameterList))}{Parameters(method.ParameterList)}", method.Span);
                break;
            }

            case ConstructorDeclarationSyntax ctor:
            {
                Add(builder, text, $"{container}..ctor{Parameters(ctor.ParameterList)}", ctor.Span);
                break;
            }

            case PropertyDeclarationSyntax property:
            {
                Add(builder, text, Combine(container, MemberName(property.ExplicitInterfaceSpecifier, property.Identifier)), property.Span);
                break;
            }

            case IndexerDeclarationSyntax indexer:
            {
                Add(builder, text, $"{container}.this{Parameters(indexer.ParameterList)}", indexer.Span);
                break;
            }

            case EventDeclarationSyntax evt:
            {
                Add(builder, text, Combine(container, MemberName(evt.ExplicitInterfaceSpecifier, evt.Identifier)), evt.Span);
                break;
            }

            default:
            {
                VisitFieldOrOperator(member, container, builder, text);
                break;
            }
        }
    }

    /// <summary>Records a field, event field or operator declaration.</summary>
    /// <param name="member">The member.</param>
    /// <param name="container">The dotted name of the enclosing namespace and types.</param>
    /// <param name="builder">The declaration builder.</param>
    /// <param name="text">The text being parsed.</param>
    private static void VisitFieldOrOperator(
        MemberDeclarationSyntax member,
        string container,
        ImmutableArray<ApiDeclaration>.Builder builder,
        SourceText text)
    {
        switch (member)
        {
            case EventFieldDeclarationSyntax eventField:
            {
                AddVariables(eventField.Declaration, container, builder, text, eventField.Span);
                break;
            }

            case FieldDeclarationSyntax field:
            {
                AddVariables(field.Declaration, container, builder, text, field.Span);
                break;
            }

            case OperatorDeclarationSyntax op:
            {
                Add(builder, text, $"{container}.op{op.OperatorToken.ValueText}{Parameters(op.ParameterList)}", op.Span);
                break;
            }

            case ConversionOperatorDeclarationSyntax conversion:
            {
                // The converted-to type is what distinguishes two conversions taking the same
                // source type, so it belongs in the identity even though a return type normally
                // does not.
                var target = RemoveWhitespace(conversion.Type.ToString());
                var kind = conversion.ImplicitOrExplicitKeyword.ValueText;
                Add(builder, text, $"{container}.op{kind}{Parameters(conversion.ParameterList)}->{target}", conversion.Span);
                break;
            }
        }
    }

    /// <summary>Records one declaration per declarator in a field or event-field declaration.</summary>
    /// <param name="declaration">The variable declaration.</param>
    /// <param name="container">The dotted name of the enclosing namespace and types.</param>
    /// <param name="builder">The declaration builder.</param>
    /// <param name="text">The text being parsed.</param>
    /// <param name="span">The span of the whole declaration.</param>
    private static void AddVariables(
        VariableDeclarationSyntax declaration,
        string container,
        ImmutableArray<ApiDeclaration>.Builder builder,
        SourceText text,
        TextSpan span)
    {
        // A hand-edited baseline may declare several fields on one line; the renderer never does.
        foreach (var variable in declaration.Variables)
        {
            Add(builder, text, Combine(container, variable.Identifier.ValueText), span);
        }
    }

    /// <summary>Adds one declaration.</summary>
    /// <param name="builder">The declaration builder.</param>
    /// <param name="text">The text being parsed.</param>
    /// <param name="identity">The declaration's identity.</param>
    /// <param name="span">The declaration's span.</param>
    private static void Add(
        ImmutableArray<ApiDeclaration>.Builder builder,
        SourceText text,
        string identity,
        TextSpan span) =>
        builder.Add(new(
            identity,
            NormalizeText(text.ToString(span)),
            text.Lines.GetLineFromPosition(span.Start).LineNumber,
            span));

    /// <summary>
    /// The declaration span of a type: everything from its first attribute up to its brace, so a
    /// type entry describes the type's own header rather than swallowing every member inside it.
    /// </summary>
    /// <param name="node">The type declaration.</param>
    /// <param name="openBrace">The type's opening brace.</param>
    /// <returns>The header span.</returns>
    private static TextSpan HeaderSpan(SyntaxNode node, SyntaxToken openBrace)
    {
        var end = openBrace.IsKind(SyntaxKind.None) ? node.Span.End : openBrace.SpanStart;
        return TextSpan.FromBounds(node.SpanStart, end > node.SpanStart ? end : node.Span.End);
    }

    /// <summary>Builds the identity of a type.</summary>
    /// <param name="qualifiedName">The type's dotted name.</param>
    /// <param name="arity">The type's generic arity.</param>
    /// <returns>The identity.</returns>
    private static string TypeIdentity(string qualifiedName, int arity) =>
        $"{qualifiedName}{ArityMarker(arity)}";

    /// <summary>Gets the number of type parameters in a list.</summary>
    /// <param name="typeParameterList">The list, or <see langword="null"/>.</param>
    /// <returns>The count.</returns>
    private static int Arity(TypeParameterListSyntax? typeParameterList) =>
        typeParameterList?.Parameters.Count ?? 0;

    /// <summary>Renders a generic arity as an identity suffix.</summary>
    /// <param name="arity">The arity.</param>
    /// <returns>The suffix, empty for a non-generic declaration.</returns>
    private static string ArityMarker(int arity) =>
        arity == 0 ? string.Empty : $"`{arity.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Joins a container and a member name.</summary>
    /// <param name="container">The container, possibly empty.</param>
    /// <param name="name">The name.</param>
    /// <returns>The dotted name.</returns>
    private static string Combine(string container, string name) =>
        container.Length == 0 ? name : $"{container}.{name}";

    /// <summary>Builds a member's name, qualified by its explicit interface when it has one.</summary>
    /// <param name="explicitInterface">The explicit interface specifier, or <see langword="null"/>.</param>
    /// <param name="identifier">The member's identifier.</param>
    /// <returns>The name.</returns>
    private static string MemberName(ExplicitInterfaceSpecifierSyntax? explicitInterface, SyntaxToken identifier) =>
        explicitInterface is null
            ? identifier.ValueText
            : $"{RemoveWhitespace(explicitInterface.ToString())}{identifier.ValueText}";

    /// <summary>
    /// Renders a parameter list the way overload identity sees it: types and reference kinds, but
    /// not names or default values. Changing a parameter name or a default keeps the same member
    /// and is reported as a change to it, not as one member replacing another.
    /// </summary>
    /// <param name="parameterList">The parameter list, or <see langword="null"/>.</param>
    /// <returns>The rendered list, including its parentheses.</returns>
    private static string Parameters(BaseParameterListSyntax? parameterList)
    {
        if (parameterList is null || parameterList.Parameters.Count == 0)
        {
            return "()";
        }

        var builder = new PooledStringBuilder();
        _ = builder.Append('(');
        var first = true;

        foreach (var parameter in parameterList.Parameters)
        {
            if (!first)
            {
                _ = builder.Append(',');
            }

            first = false;
            AppendParameterIdentity(builder, parameter);
        }

        return builder.Append(')').ToString();
    }

    /// <summary>Determines whether a parameter modifier changes the overload signature.</summary>
    /// <param name="modifier">The modifier token.</param>
    /// <returns><see langword="true"/> for a reference-kind modifier.</returns>
    private static bool IsReferenceKind(SyntaxToken modifier) =>
        modifier.IsKind(SyntaxKind.RefKeyword)
            || modifier.IsKind(SyntaxKind.OutKeyword)
            || modifier.IsKind(SyntaxKind.InKeyword)
            || modifier.IsKind(SyntaxKind.ReadOnlyKeyword);

    /// <summary>
    /// Strips the indentation a declaration carries because of where it sits in the file, so the
    /// same member compares equal regardless of nesting depth and reads cleanly in a diagnostic.
    /// </summary>
    /// <param name="raw">The raw declaration text.</param>
    /// <returns>The normalized text.</returns>
    private static string NormalizeText(string raw)
    {
        var builder = new PooledStringBuilder(raw.Length);
        var start = 0;
        var first = true;

        while (start <= raw.Length)
        {
            var end = raw.IndexOf('\n', start);
            var lineEnd = end < 0 ? raw.Length : end;
            var line = raw.Substring(start, lineEnd - start).Trim();

            if (line.Length > 0)
            {
                _ = first ? builder : builder.Append('\n');
                _ = builder.Append(line);
                first = false;
            }

            if (end < 0)
            {
                break;
            }

            start = end + 1;
        }

        return builder.ToString();
    }

    /// <summary>Removes every whitespace character, so spacing cannot affect an identity.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value without whitespace.</returns>
    private static string RemoveWhitespace(string value)
    {
        var builder = new PooledStringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            _ = builder.Append(c);
        }

        return builder.ToString();
    }
}
