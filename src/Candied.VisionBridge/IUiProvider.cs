using System.Collections.Generic;
using Candied.VisionBridge.Protocol;

namespace Candied.VisionBridge;

public interface IUiProvider
{
    IList<VisionUiElementDto> GetInteractiveElements();
}
