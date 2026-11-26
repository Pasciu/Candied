using System;
using System.Collections.Generic;
using System.Linq;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Candied.ArchitectureTests;

public class ArchitectureTests
{
    private static readonly Architecture Architecture = BuildArchitecture();

    private static Architecture BuildArchitecture()
    {
        var assemblies = new List<System.Reflection.Assembly>
        {
            typeof(Primitives.Result<,>).Assembly,
            typeof(Core.Models.TargetDescriptor).Assembly,
            typeof(Vision.Protocol.VisionConstants).Assembly,
            typeof(Vision.DependencyInjection).Assembly,
            typeof(Infrastructure.DependencyInjection).Assembly,
            typeof(Platforms.Unity.Shared.DependencyInjection).Assembly,
            typeof(Platforms.Unity.Mono.DependencyInjection).Assembly,
            typeof(Agent.Program).Assembly
        };

#if WINDOWS
        assemblies.Add(typeof(Studio.App).Assembly);
#endif

        return new ArchLoader().LoadAssemblies(assemblies.ToArray()).Build();
    }

    private readonly IObjectProvider<IType> PrimitivesLayer =
        Types().That().ResideInAssembly(typeof(Primitives.Result<,>).Assembly).As("Primitives Layer");

    private readonly IObjectProvider<IType> CoreLayer =
        Types().That().ResideInAssembly(typeof(Core.Models.TargetDescriptor).Assembly).As("Core Layer");

    private readonly IObjectProvider<IType> VisionProtocolLayer =
        Types().That().ResideInAssembly(typeof(Vision.Protocol.VisionConstants).Assembly).As("Vision Protocol Layer");

    private readonly IObjectProvider<IType> VisionLayer =
        Types().That().ResideInAssembly(typeof(Vision.DependencyInjection).Assembly).As("Vision Layer");

    private readonly IObjectProvider<IType> InfrastructureLayer =
        Types().That().ResideInAssembly(typeof(Infrastructure.DependencyInjection).Assembly).As("Infrastructure Layer");

    private readonly IObjectProvider<IType> UnitySharedPlatformLayer =
        Types().That().ResideInAssembly(typeof(Platforms.Unity.Shared.DependencyInjection).Assembly).As("Unity Shared Platform Layer");

    private readonly IObjectProvider<IType> UnityMonoPlatformLayer =
        Types().That().ResideInAssembly(typeof(Platforms.Unity.Mono.DependencyInjection).Assembly).As("Unity Mono Platform Layer");

    private readonly IObjectProvider<IType> AgentLayer =
        Types().That().ResideInAssembly(typeof(Agent.Program).Assembly).As("Agent Layer");

#if WINDOWS
    private readonly IObjectProvider<IType> StudioLayer =
        Types().That().ResideInAssembly(typeof(Studio.App).Assembly).As("Studio Host Layer");
#endif

    [Fact]
    public void Primitives_Must_Not_Depend_On_Any_Other_Module()
    {
        IArchRule rule = Types().That().Are(PrimitivesLayer)
            .Should().NotDependOnAny(CoreLayer)
            .AndShould().NotDependOnAny(VisionProtocolLayer)
            .AndShould().NotDependOnAny(VisionLayer)
            .AndShould().NotDependOnAny(InfrastructureLayer)
            .AndShould().NotDependOnAny(UnitySharedPlatformLayer)
            .AndShould().NotDependOnAny(UnityMonoPlatformLayer)
            .AndShould().NotDependOnAny(AgentLayer);

        rule.Check(Architecture);
    }

    [Fact]
    public void Core_Must_Not_Depend_On_Infrastructure_Vision_Platforms_Studio_Or_Agent()
    {
        IArchRule rule = Types().That().Are(CoreLayer)
            .Should().NotDependOnAny(VisionProtocolLayer)
            .AndShould().NotDependOnAny(VisionLayer)
            .AndShould().NotDependOnAny(InfrastructureLayer)
            .AndShould().NotDependOnAny(UnitySharedPlatformLayer)
            .AndShould().NotDependOnAny(UnityMonoPlatformLayer)
#if WINDOWS
            .AndShould().NotDependOnAny(StudioLayer)
#endif
            .AndShould().NotDependOnAny(AgentLayer);

        rule.Check(Architecture);
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
    public void Core_And_Primitives_Must_Be_Completely_Engine_Agnostic()
    {
        string[] forbiddenTerms = ["Unity", "BepInEx", "Il2Cpp", "Harmony", "PCSX2", "Dolphin"];

        var coreAndPrimitiveTypes = new[]
        {
            typeof(Primitives.Result<,>).Assembly,
            typeof(Core.Models.TargetDescriptor).Assembly
        }.SelectMany(a => a.GetTypes());

        foreach (var type in coreAndPrimitiveTypes)
        {
            foreach (var term in forbiddenTerms)
            {
                Assert.DoesNotContain(term, type.FullName ?? type.Name, StringComparison.OrdinalIgnoreCase);

                foreach (var prop in type.GetProperties())
                {
                    Assert.DoesNotContain(term, prop.Name, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void Vision_Layers_Must_Not_Depend_On_Core_Infrastructure_Platforms_Or_Agent()
    {
        IArchRule protocolRule = Types().That().Are(VisionProtocolLayer)
            .Should().NotDependOnAny(PrimitivesLayer)
            .AndShould().NotDependOnAny(CoreLayer)
            .AndShould().NotDependOnAny(VisionLayer)
            .AndShould().NotDependOnAny(InfrastructureLayer)
            .AndShould().NotDependOnAny(UnitySharedPlatformLayer)
            .AndShould().NotDependOnAny(UnityMonoPlatformLayer)
            .AndShould().NotDependOnAny(AgentLayer);

        IArchRule visionRule = Types().That().Are(VisionLayer)
            .Should().NotDependOnAny(CoreLayer)
            .AndShould().NotDependOnAny(InfrastructureLayer)
            .AndShould().NotDependOnAny(UnitySharedPlatformLayer)
            .AndShould().NotDependOnAny(UnityMonoPlatformLayer)
#if WINDOWS
            .AndShould().NotDependOnAny(StudioLayer)
#endif
            .AndShould().NotDependOnAny(AgentLayer);

        protocolRule.Check(Architecture);
        visionRule.Check(Architecture);
    }

#if WINDOWS
    [Fact]
    public void Studio_Must_Be_Pure_Vision_And_Window_Host_Without_Core_Or_Modding_Dependencies()
    {
        IArchRule rule = Types().That().Are(StudioLayer)
            .Should().NotDependOnAny(CoreLayer)
            .AndShould().NotDependOnAny(InfrastructureLayer)
            .AndShould().NotDependOnAny(UnitySharedPlatformLayer)
            .AndShould().NotDependOnAny(UnityMonoPlatformLayer)
            .AndShould().NotDependOnAny(AgentLayer);

        rule.Check(Architecture);
    }
#endif

    [Fact]
    public void Infrastructure_And_Platform_Modules_Must_Remain_Decoupled()
    {
        IArchRule infraRule = Types().That().Are(InfrastructureLayer)
            .Should().NotDependOnAny(VisionLayer)
            .AndShould().NotDependOnAny(UnitySharedPlatformLayer)
            .AndShould().NotDependOnAny(UnityMonoPlatformLayer)
#if WINDOWS
            .AndShould().NotDependOnAny(StudioLayer)
#endif
            .AndShould().NotDependOnAny(AgentLayer);

        IArchRule platformRule = Types().That().Are(UnitySharedPlatformLayer)
            .Or().Are(UnityMonoPlatformLayer)
            .Should().NotDependOnAny(InfrastructureLayer)
#if WINDOWS
            .AndShould().NotDependOnAny(StudioLayer)
#endif
            .AndShould().NotDependOnAny(AgentLayer);

        infraRule.Check(Architecture);
        platformRule.Check(Architecture);
    }

    [Fact]
    public void Concrete_Adapters_And_Platform_Implementations_Must_Be_Internal()
    {
        IArchRule infraAdapters = Classes().That().ResideInNamespaceMatching(@"^Candied\.Infrastructure\.(Adapters|Internal).*")
            .Should().NotBePublic();

        IArchRule visionAdapters = Classes().That().ResideInNamespaceMatching(@"^Candied\.Vision\.Adapters.*")
            .Should().NotBePublic();

        IArchRule unitySharedImpls = Classes().That().ResideInNamespaceMatching(@"^Candied\.Platforms\.Unity\.Shared\.(Capabilities|Inspection|Input|Internal).*")
            .Should().NotBePublic();

        IArchRule unityMonoImpls = Classes().That().ResideInNamespaceMatching(@"^Candied\.Platforms\.Unity\.Mono\.(Capabilities|Internal|Tools).*")
            .And().DoNotHaveName("DependencyInjection")
            .Should().NotBePublic();

        infraAdapters.Check(Architecture);
        visionAdapters.Check(Architecture);
        unitySharedImpls.Check(Architecture);
        unityMonoImpls.Check(Architecture);
    }

    [Fact]
    public void Adapters_Must_Not_Implement_Multiple_Ports()
    {
        var portInterfaces = new[]
        {
            typeof(Core.Ports.ISymbolIndex).Assembly,
            typeof(Vision.Ports.IVisionSensorClient).Assembly
        }
        .SelectMany(a => a.GetTypes())
        .Where(t => t.IsInterface && (t.Namespace == "Candied.Core.Ports" || t.Namespace == "Candied.Vision.Ports"))
        .ToList();

        var adapterTypes = new[]
        {
            typeof(Infrastructure.DependencyInjection).Assembly,
            typeof(Vision.DependencyInjection).Assembly
        }
        .SelectMany(a => a.GetTypes())
        .Where(t => t.IsClass && !t.IsAbstract && t.Namespace != null && t.Namespace.EndsWith(".Adapters", StringComparison.Ordinal))
        .ToList();

        Assert.NotEmpty(adapterTypes);

        foreach (var adapter in adapterTypes)
        {
            var implementedPorts = portInterfaces.Where(p => p.IsAssignableFrom(adapter)).ToList();
            Assert.True(
                implementedPorts.Count <= 1,
                $"Adapter '{adapter.Name}' implements multiple ports ({string.Join(", ", implementedPorts.Select(p => p.Name))}). Each adapter must implement at most one port.");
        }
    }
}
