using Candied.Core.Analysis;
using Xunit;

namespace Candied.Tests.Unit;

public class TypeMemberSourceExtractorTests
{
    private const string SampleClass = """
        namespace SampleGame.Actors;

        public class CharacterRig
        {
            private float _speed = 1.5f;

            [SerializeField]
            private string _idleTrigger = "Idle {brace}";

            public string DisplayName { get; set; } = "Default";

            public void PlayIdleAnimation()
            {
                if (_speed > 0f)
                {
                    var label = "brace } inside string";
                    // comment with { unmatched brace
                    Trigger(_idleTrigger + label);
                }
            }

            public void UpdateWalkCycle(float dt)
            {
                _speed += dt;
            }
        }
        """;

    [Fact]
    public void ExtractMatchingMembers_ReturnsOnlyMatchingMethodAndField()
    {
        var extracted = TypeMemberSourceExtractor.ExtractMatchingMembers(SampleClass, "Idle");

        Assert.Contains("PlayIdleAnimation", extracted);
        Assert.Contains("_idleTrigger", extracted);
        Assert.Contains("Trigger(_idleTrigger + label);", extracted);
        Assert.DoesNotContain("UpdateWalkCycle", extracted);
        Assert.DoesNotContain("DisplayName", extracted);
    }

    [Fact]
    public void ExtractMatchingMembers_PreservesAttributesAndHandlesStringsWithBraces()
    {
        var extracted = TypeMemberSourceExtractor.ExtractMatchingMembers(SampleClass, "PlayIdleAnimation");

        Assert.Contains("public void PlayIdleAnimation()", extracted);
        Assert.Contains("var label = \"brace } inside string\";", extracted);
        Assert.DoesNotContain("UpdateWalkCycle", extracted);
    }

    [Fact]
    public void ExtractMatchingMembers_WhenNoMemberMatches_ReturnsEmptyString()
    {
        var extracted = TypeMemberSourceExtractor.ExtractMatchingMembers(SampleClass, "NonExistentMember");

        Assert.Equal(string.Empty, extracted);
    }
}
