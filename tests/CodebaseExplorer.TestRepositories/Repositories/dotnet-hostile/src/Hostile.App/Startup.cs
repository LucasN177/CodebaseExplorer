using System.Runtime.CompilerServices;

namespace Hostile.App;

// HOSTILE FIXTURE (QA-003): runs only if this code is compiled AND loaded, which Codebase Explorer never does.
internal static class Startup
{
    [ModuleInitializer]
    internal static void WriteMarker() =>
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "MARKER-module-initializer.txt"), "executed");
}
