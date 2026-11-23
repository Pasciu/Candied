using Candied.Core.Models;
using Candied.Decompiler.Internal;
using Xunit;

namespace Candied.Tests.Unit;

public class RoslynSymbolExtractorTests
{
    private const string SampleCode = @"
namespace GameLogic.Characters;

public interface IDamageable
{
    void TakeDamage(float amount);
}

public class BaseCharacter
{
    protected float _speed = 5.0f;
}

public class PlayerController : BaseCharacter, IDamageable
{
    private float _health = 100f;
    public string PlayerName { get; set; } = ""Knight"";

    public void TakeDamage(float amount)
    {
        _health -= amount;
    }

    public bool IsAlive() => _health > 0;
}
";

    [Fact]
    public void ExtractFromFile_Extracts_All_Types_And_Members()
    {
        var (symbols, details) = RoslynSymbolExtractor.ExtractFromFile(
            SampleCode,
            "Characters/PlayerController.cs",
            "Assembly-CSharp");

        Assert.NotEmpty(symbols);
        Assert.Equal(3, details.Count); // IDamageable, BaseCharacter, PlayerController

        // Check types
        Assert.Contains(symbols, s => s.FullName == "GameLogic.Characters.IDamageable" && s.Kind == SymbolKind.Interface);
        Assert.Contains(symbols, s => s.FullName == "GameLogic.Characters.BaseCharacter" && s.Kind == SymbolKind.Class);
        Assert.Contains(symbols, s => s.FullName == "GameLogic.Characters.PlayerController" && s.Kind == SymbolKind.Class);

        // Check members
        Assert.Contains(symbols, s => s.FullName.StartsWith("GameLogic.Characters.PlayerController.TakeDamage", StringComparison.Ordinal) && s.Kind == SymbolKind.Method);
        Assert.Contains(symbols, s => s.FullName == "GameLogic.Characters.PlayerController._health" && s.Kind == SymbolKind.Field);
        Assert.Contains(symbols, s => s.FullName == "GameLogic.Characters.PlayerController.PlayerName" && s.Kind == SymbolKind.Property);

        // Check type detail
        var playerDetail = details.First(d => d.FullName == "GameLogic.Characters.PlayerController");
        Assert.Equal("BaseCharacter", playerDetail.BaseType);
        Assert.Contains("IDamageable", playerDetail.Interfaces);
        Assert.Contains(playerDetail.FieldSignatures, f => f.Contains("_health"));
        Assert.Contains(playerDetail.MethodSignatures, m => m.Contains("TakeDamage"));
    }

    [Fact]
    public void ExtractFromFile_Extracts_Nested_Types_Constructors_And_Events()
    {
        const string code = @"
namespace GameLogic.Core;

public class GameSession
{
    public event System.Action<int> OnStateChanged;

    public GameSession(string sessionId)
    {
    }

    public class SessionConfig
    {
        public int MaxPlayers = 4;
    }
}
";
        var (symbols, details) = RoslynSymbolExtractor.ExtractFromFile(code, "Core/GameSession.cs", "GameAssembly");

        Assert.Equal(2, details.Count);

        // Types
        Assert.Contains(symbols, s => s.FullName == "GameLogic.Core.GameSession" && s.Kind == SymbolKind.Class);
        Assert.Contains(symbols, s => s.FullName == "GameLogic.Core.GameSession.SessionConfig" && s.Kind == SymbolKind.Class);

        // Constructor
        Assert.Contains(symbols, s => s.FullName.StartsWith("GameLogic.Core.GameSession.GameSession", StringComparison.Ordinal) && s.Kind == SymbolKind.Method);

        // Event
        Assert.Contains(symbols, s => s.FullName == "GameLogic.Core.GameSession.OnStateChanged" && s.Kind == SymbolKind.Event);

        // Nested class field
        Assert.Contains(symbols, s => s.FullName == "GameLogic.Core.GameSession.SessionConfig.MaxPlayers" && s.Kind == SymbolKind.Field);
    }

    [Fact]
    public void ExtractFromFile_Handles_FileScopedNamespace_And_Records()
    {
        const string code = @"
namespace GameLogic.Items;

public record ItemData(int Id, string Name);
";
        var (symbols, details) = RoslynSymbolExtractor.ExtractFromFile(code, "Items/ItemData.cs", "GameAssembly");

        Assert.Single(details);
        Assert.Contains(symbols, s => s.FullName == "GameLogic.Items.ItemData" && s.Kind == SymbolKind.Class);
        Assert.Equal("GameLogic.Items.ItemData", details[0].FullName);
    }

    [Fact]
    public void ExtractFromFile_Handles_Empty_Or_Whitespace()
    {
        var (symbols1, details1) = RoslynSymbolExtractor.ExtractFromFile("", "empty.cs", "asm");
        Assert.Empty(symbols1);
        Assert.Empty(details1);

        var (symbols2, details2) = RoslynSymbolExtractor.ExtractFromFile("   \r\n   ", "whitespace.cs", "asm");
        Assert.Empty(symbols2);
        Assert.Empty(details2);
    }
}
