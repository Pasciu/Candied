using System.Collections.Generic;

namespace Candied.Core.Models;

public enum SymbolKind
{
    Class,
    Struct,
    Interface,
    Method,
    Field,
    Property,
    Event
}

public enum SymbolKindFilter
{
    All,
    TypeOnly,
    MemberOnly
}

public sealed record SymbolQuery(
    string SearchTerm,
    SymbolKindFilter Filter = SymbolKindFilter.All,
    string? NamespacePrefix = null,
    int Limit = 25,
    SymbolKind? ExactKind = null,
    bool EnumOnly = false,
    string? ModuleFilter = null
);

public sealed record SymbolSummary(
    string FullName,
    SymbolKind Kind,
    string Namespace,
    string ModuleName,
    string Signature,
    string RelativeSourcePath
);

public sealed record TypeDefinitionDetail(
    string FullName,
    string Namespace,
    string? BaseType,
    IReadOnlyList<string> Interfaces,
    IReadOnlyList<string> FieldSignatures,
    IReadOnlyList<string> MethodSignatures,
    string RawSourceCode,
    string? ModuleName = null,
    string? RelativeSourcePath = null
);

public sealed record CodeSearchQuery(
    string SearchTerm,
    string? NamespacePrefix = null,
    string? ModuleFilter = null,
    int Limit = 25
);

public sealed record CodeSearchMatch(
    string FullName,
    string Namespace,
    string? ModuleName,
    string? RelativeSourcePath,
    int LineNumber,
    string Snippet
);
