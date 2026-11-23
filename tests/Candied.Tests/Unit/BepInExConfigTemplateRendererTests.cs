using Candied.Workspace.Rendering;
using Xunit;

namespace Candied.Tests.Unit;

public class BepInExConfigTemplateRendererTests
{
    [Fact]
    public void RenderDefaultConfig_WithConsoleEnabled_Generates_Console_True()
    {
        var config = BepInExConfigTemplateRenderer.RenderDefaultConfig(enableConsole: true);

        Assert.Contains("[Logging.Console]", config);
        Assert.Contains("Enabled = true", config);
        Assert.Contains("[Logging.Disk]", config);
        Assert.Contains("WriteUnityLog = true", config);
    }

    [Fact]
    public void RenderDefaultConfig_WithConsoleDisabled_Generates_Console_False()
    {
        var config = BepInExConfigTemplateRenderer.RenderDefaultConfig(enableConsole: false);

        Assert.Contains("[Logging.Console]", config);
        Assert.Contains("Enabled = false", config);
    }
}
