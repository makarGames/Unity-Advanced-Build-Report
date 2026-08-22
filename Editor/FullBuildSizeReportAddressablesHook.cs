using System;
using System.Collections.Generic;

namespace DetPanda.Editor.FullBuildSizeReport
{
    /// <summary>
    /// Cross-assembly bridge: Addressables optional assembly registers Collect at load time.
    /// Main Editor asmdef must not reference Addressables types directly.
    /// </summary>
    public static class FullBuildSizeReportAddressablesHook
    {
        public static Action<FullBuildSizeReportResult, Dictionary<string, FullBuildSizeReportEntry>> Collect;
    }
}
