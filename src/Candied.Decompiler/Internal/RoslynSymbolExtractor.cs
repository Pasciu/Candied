using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Candied.Core.Models;
using CoreSymbolKind = Candied.Core.Models.SymbolKind;

namespace Candied.Decompiler.Internal;

internal static class RoslynSymbolExtractor
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    public static (List<SymbolSummary> Symbols, List<TypeDefinitionDetail> TypeDetails) ExtractFromFile(
        string sourceCode,
        string relativeFilePath,
        string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            return ([], []);
        }

        var symbols = new List<SymbolSummary>();
        var typeDetails = new List<TypeDefinitionDetail>();

        var tree = CSharpSyntaxTree.ParseText(sourceCode, ParseOptions);
        var root = tree.GetCompilationUnitRoot();

        var typeDeclarations = CollectTypeDeclarations(root.Members);

        foreach (var typeDecl in typeDeclarations)
        {
            var typeNamespace = GetNamespace(typeDecl);
            var fullTypeName = GetFullTypeName(typeDecl, typeNamespace);
            var typeName = typeDecl.Identifier.Text;

            var kind = typeDecl switch
            {
                ClassDeclarationSyntax => CoreSymbolKind.Class,
                StructDeclarationSyntax => CoreSymbolKind.Struct,
                InterfaceDeclarationSyntax => CoreSymbolKind.Interface,
                EnumDeclarationSyntax => CoreSymbolKind.Class,
                RecordDeclarationSyntax => CoreSymbolKind.Class,
                _ => CoreSymbolKind.Class
            };

            var modifiers = typeDecl.Modifiers.ToString();
            var keyword = typeDecl switch
            {
                ClassDeclarationSyntax => "class",
                StructDeclarationSyntax => "struct",
                InterfaceDeclarationSyntax => "interface",
                EnumDeclarationSyntax => "enum",
                RecordDeclarationSyntax => "record",
                _ => "type"
            };

            var baseListStr = typeDecl.BaseList != null ? $" : {typeDecl.BaseList.Types}" : string.Empty;
            var typeSignature = string.IsNullOrEmpty(modifiers)
                ? $"{keyword} {typeName}{baseListStr}"
                : $"{modifiers} {keyword} {typeName}{baseListStr}";

            // Add the type symbol
            symbols.Add(new SymbolSummary(
                FullName: fullTypeName,
                Kind: kind,
                Namespace: typeNamespace,
                AssemblyName: assemblyName,
                Signature: typeSignature,
                RelativeSourcePath: relativeFilePath
            ));

            string? baseType = null;
            var interfaces = new List<string>();
            if (typeDecl.BaseList != null)
            {
                bool isFirst = true;
                foreach (var baseTypeSyntax in typeDecl.BaseList.Types)
                {
                    var baseTypeName = baseTypeSyntax.Type.ToString();
                    if (isFirst && kind == CoreSymbolKind.Class && !baseTypeName.StartsWith('I'))
                    {
                        baseType = baseTypeName;
                    }
                    else
                    {
                        interfaces.Add(baseTypeName);
                    }
                    isFirst = false;
                }
            }

            var fieldSignatures = new List<string>();
            var methodSignatures = new List<string>();

            // Extract members for TypeDeclarationSyntax (class, struct, interface, record)
            if (typeDecl is TypeDeclarationSyntax typeDef)
            {
                foreach (var member in typeDef.Members)
                {
                    if (member is FieldDeclarationSyntax field)
                    {
                        var fieldType = field.Declaration.Type.ToString();
                        var fieldModifiers = field.Modifiers.ToString();
                        foreach (var variable in field.Declaration.Variables)
                        {
                            var fieldName = variable.Identifier.Text;
                            var fieldSig = string.IsNullOrEmpty(fieldModifiers)
                                ? $"{fieldType} {fieldName}"
                                : $"{fieldModifiers} {fieldType} {fieldName}";

                            fieldSignatures.Add(fieldSig);

                            symbols.Add(new SymbolSummary(
                                FullName: $"{fullTypeName}.{fieldName}",
                                Kind: CoreSymbolKind.Field,
                                Namespace: typeNamespace,
                                AssemblyName: assemblyName,
                                Signature: fieldSig,
                                RelativeSourcePath: relativeFilePath
                            ));
                        }
                    }
                    else if (member is PropertyDeclarationSyntax prop)
                    {
                        var propType = prop.Type.ToString();
                        var propModifiers = prop.Modifiers.ToString();
                        var propName = prop.Identifier.Text;
                        var propSig = string.IsNullOrEmpty(propModifiers)
                            ? $"{propType} {propName} {{ get; set; }}"
                            : $"{propModifiers} {propType} {propName} {{ get; set; }}";

                        symbols.Add(new SymbolSummary(
                            FullName: $"{fullTypeName}.{propName}",
                            Kind: CoreSymbolKind.Property,
                            Namespace: typeNamespace,
                            AssemblyName: assemblyName,
                            Signature: propSig,
                            RelativeSourcePath: relativeFilePath
                        ));
                    }
                    else if (member is MethodDeclarationSyntax method)
                    {
                        var returnType = method.ReturnType.ToString();
                        var methodModifiers = method.Modifiers.ToString();
                        var methodName = method.Identifier.Text;
                        var paramList = method.ParameterList.ToString();
                        var methodSig = string.IsNullOrEmpty(methodModifiers)
                            ? $"{returnType} {methodName}{paramList}"
                            : $"{methodModifiers} {returnType} {methodName}{paramList}";

                        methodSignatures.Add(methodSig);

                        symbols.Add(new SymbolSummary(
                            FullName: $"{fullTypeName}.{methodName}{paramList}",
                            Kind: CoreSymbolKind.Method,
                            Namespace: typeNamespace,
                            AssemblyName: assemblyName,
                            Signature: methodSig,
                            RelativeSourcePath: relativeFilePath
                        ));
                    }
                    else if (member is ConstructorDeclarationSyntax ctor)
                    {
                        var ctorModifiers = ctor.Modifiers.ToString();
                        var ctorName = ctor.Identifier.Text;
                        var paramList = ctor.ParameterList.ToString();
                        var ctorSig = string.IsNullOrEmpty(ctorModifiers)
                            ? $"{ctorName}{paramList}"
                            : $"{ctorModifiers} {ctorName}{paramList}";

                        methodSignatures.Add(ctorSig);

                        symbols.Add(new SymbolSummary(
                            FullName: $"{fullTypeName}.{ctorName}{paramList}",
                            Kind: CoreSymbolKind.Method,
                            Namespace: typeNamespace,
                            AssemblyName: assemblyName,
                            Signature: ctorSig,
                            RelativeSourcePath: relativeFilePath
                        ));
                    }
                    else if (member is EventDeclarationSyntax evt)
                    {
                        var evtType = evt.Type.ToString();
                        var evtModifiers = evt.Modifiers.ToString();
                        var evtName = evt.Identifier.Text;
                        var evtSig = string.IsNullOrEmpty(evtModifiers)
                            ? $"event {evtType} {evtName}"
                            : $"{evtModifiers} event {evtType} {evtName}";

                        symbols.Add(new SymbolSummary(
                            FullName: $"{fullTypeName}.{evtName}",
                            Kind: CoreSymbolKind.Event,
                            Namespace: typeNamespace,
                            AssemblyName: assemblyName,
                            Signature: evtSig,
                            RelativeSourcePath: relativeFilePath
                        ));
                    }
                    else if (member is EventFieldDeclarationSyntax evtField)
                    {
                        var evtType = evtField.Declaration.Type.ToString();
                        var evtModifiers = evtField.Modifiers.ToString();
                        foreach (var variable in evtField.Declaration.Variables)
                        {
                            var evtName = variable.Identifier.Text;
                            var evtSig = string.IsNullOrEmpty(evtModifiers)
                                ? $"event {evtType} {evtName}"
                                : $"{evtModifiers} event {evtType} {evtName}";

                            symbols.Add(new SymbolSummary(
                                FullName: $"{fullTypeName}.{evtName}",
                                Kind: CoreSymbolKind.Event,
                                Namespace: typeNamespace,
                                AssemblyName: assemblyName,
                                Signature: evtSig,
                                RelativeSourcePath: relativeFilePath
                            ));
                        }
                    }
                }
            }

            typeDetails.Add(new TypeDefinitionDetail(
                FullName: fullTypeName,
                Namespace: typeNamespace,
                BaseType: baseType,
                Interfaces: interfaces,
                FieldSignatures: fieldSignatures,
                MethodSignatures: methodSignatures,
                RawSourceCode: typeDecl.ToFullString().Trim()
            ));
        }

        return (symbols, typeDetails);
    }

    private static List<BaseTypeDeclarationSyntax> CollectTypeDeclarations(SyntaxList<MemberDeclarationSyntax> members)
    {
        var result = new List<BaseTypeDeclarationSyntax>();
        CollectTypeDeclarationsRecursive(members, result);
        return result;
    }

    private static void CollectTypeDeclarationsRecursive(
        SyntaxList<MemberDeclarationSyntax> members,
        List<BaseTypeDeclarationSyntax> result)
    {
        foreach (var member in members)
        {
            if (member is BaseTypeDeclarationSyntax typeDecl)
            {
                result.Add(typeDecl);
                if (typeDecl is TypeDeclarationSyntax typeWithMembers)
                {
                    CollectTypeDeclarationsRecursive(typeWithMembers.Members, result);
                }
            }
            else if (member is BaseNamespaceDeclarationSyntax nsDecl)
            {
                CollectTypeDeclarationsRecursive(nsDecl.Members, result);
            }
        }
    }

    private static string GetNamespace(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is BaseNamespaceDeclarationSyntax ns)
            {
                return ns.Name.ToString();
            }
            parent = parent.Parent;
        }
        return string.Empty;
    }

    private static string GetFullTypeName(BaseTypeDeclarationSyntax typeDecl, string typeNamespace)
    {
        var enclosingTypes = new List<string>();
        var parent = typeDecl.Parent;
        while (parent != null)
        {
            if (parent is BaseTypeDeclarationSyntax enclosingType)
            {
                enclosingTypes.Add(enclosingType.Identifier.Text);
            }
            parent = parent.Parent;
        }

        enclosingTypes.Reverse();
        enclosingTypes.Add(typeDecl.Identifier.Text);
        var typeHierarchyName = string.Join(".", enclosingTypes);

        return string.IsNullOrEmpty(typeNamespace) ? typeHierarchyName : $"{typeNamespace}.{typeHierarchyName}";
    }
}
