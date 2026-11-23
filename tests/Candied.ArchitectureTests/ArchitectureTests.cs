using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Candied.ArchitectureTests;

public class ArchitectureTests
{
    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(Primitives.Result<,>).Assembly,
            typeof(Core.Models.GameEnvironment).Assembly,
            typeof(Decompiler.DependencyInjection).Assembly,
            typeof(Workspace.DependencyInjection).Assembly,
            typeof(Agent.Program).Assembly)
        .Build();

    private readonly IObjectProvider<IType> CoreLayer =
        Types().That().ResideInAssembly(typeof(Core.Models.GameEnvironment).Assembly).As("Core Layer");

    private readonly IObjectProvider<IType> PrimitivesLayer =
        Types().That().ResideInAssembly(typeof(Primitives.Result<,>).Assembly).As("Primitives Layer");

    private readonly IObjectProvider<IType> DecompilerLayer =
        Types().That().ResideInAssembly(typeof(Decompiler.DependencyInjection).Assembly).As("Decompiler Layer");

    private readonly IObjectProvider<IType> WorkspaceLayer =
        Types().That().ResideInAssembly(typeof(Workspace.DependencyInjection).Assembly).As("Workspace Layer");

    private readonly IObjectProvider<IType> AgentLayer =
        Types().That().ResideInAssembly(typeof(Agent.Program).Assembly).As("Agent Layer");

    [Fact]
    public void Core_Must_Not_Depend_On_Infrastructure_Or_Agent()
    {
        IArchRule rule = Types().That().Are(CoreLayer)
            .Should().NotDependOnAny(DecompilerLayer)
            .AndShould().NotDependOnAny(WorkspaceLayer)
            .AndShould().NotDependOnAny(AgentLayer);

        rule.Check(Architecture);
    }

    [Fact]
    public void Decompiler_And_Workspace_Must_Not_Depend_On_Each_Other()
    {
        IArchRule decompilerRule = Types().That().Are(DecompilerLayer)
            .Should().NotDependOnAny(WorkspaceLayer);

        IArchRule workspaceRule = Types().That().Are(WorkspaceLayer)
            .Should().NotDependOnAny(DecompilerLayer);

        decompilerRule.Check(Architecture);
        workspaceRule.Check(Architecture);
    }

    [Fact]
    public void Primitives_Must_Not_Depend_On_Any_Other_Module()
    {
        IArchRule rule = Types().That().Are(PrimitivesLayer)
            .Should().NotDependOnAny(CoreLayer)
            .AndShould().NotDependOnAny(DecompilerLayer)
            .AndShould().NotDependOnAny(WorkspaceLayer)
            .AndShould().NotDependOnAny(AgentLayer);

        rule.Check(Architecture);
    }

    [Fact]
    public void Infrastructure_Adapters_Must_Not_Be_Public()
    {
        IArchRule decompilerAdapters = Classes().That().ResideInNamespace("Candied.Decompiler.Adapters")
            .Should().NotBePublic();

        IArchRule workspaceAdapters = Classes().That().ResideInNamespace("Candied.Workspace.Adapters")
            .Should().NotBePublic();

        decompilerAdapters.Check(Architecture);
        workspaceAdapters.Check(Architecture);
    }

    [Fact]
    public void Core_Must_Not_Directly_Use_System_IO_Or_Process()
    {
        IArchRule rule = Types().That().Are(CoreLayer)
            .Should().NotDependOnAny(
                Types().That().ResideInNamespaceMatching(@"^System\.IO.*")
                .Or().ResideInNamespaceMatching(@"^System\.Diagnostics\.Process.*"));

        rule.Check(Architecture);
    }

    [Fact]
    public void Adapters_Must_Not_Implement_Multiple_Ports()
    {
        var portInterfaces = typeof(Core.Ports.IGameDetector).Assembly.GetTypes()
            .Where(t => t.IsInterface && t.Namespace == "Candied.Core.Ports")
            .ToList();

        var adapterTypes = new[]
        {
            typeof(Decompiler.DependencyInjection).Assembly,
            typeof(Workspace.DependencyInjection).Assembly
        }
        .SelectMany(a => a.GetTypes())
        .Where(t => t.IsClass && !t.IsAbstract && t.Namespace != null && t.Namespace.EndsWith(".Adapters", StringComparison.Ordinal))
        .ToList();

        foreach (var adapter in adapterTypes)
        {
            var implementedPorts = portInterfaces.Where(p => p.IsAssignableFrom(adapter)).ToList();
            Assert.True(
                implementedPorts.Count <= 1,
                $"Adapter '{adapter.Name}' implements multiple ports ({string.Join(", ", implementedPorts.Select(p => p.Name))}). Each adapter must implement at most one port to honor the Single Responsibility Principle.");
        }
    }
}
