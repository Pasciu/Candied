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

public record SymbolQuery(
    string SearchTerm,
    SymbolKindFilter Filter = SymbolKindFilter.All,
    string? NamespacePrefix = null,
    int Limit = 25,
    SymbolKind? ExactKind = null,
    bool EnumOnly = false
);

public record SymbolSummary(
    string FullName,
    SymbolKind Kind,
    string Namespace,
    string AssemblyName,
    string Signature,
    string RelativeSourcePath
);

public record TypeDefinitionDetail(
    string FullName,
    string Namespace,
    string? BaseType,
    IReadOnlyList<string> Interfaces,
    IReadOnlyList<string> FieldSignatures,
    IReadOnlyList<string> MethodSignatures,
    string RawSourceCode,
    string? AssemblyName = null,
    string? RelativeSourcePath = null
);

public record CodeSearchQuery(
    string SearchTerm,
    string? NamespacePrefix = null,
    int Limit = 25
);

public record CodeSearchMatch(
    string FullName,
    string Namespace,
    string? AssemblyName,
    string? RelativeSourcePath,
    int LineNumber,
    string Snippet
);
