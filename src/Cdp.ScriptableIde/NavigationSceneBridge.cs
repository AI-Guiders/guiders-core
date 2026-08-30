#nullable enable
using AIGuiders.Platform.Navigation.Code;
using AIGuiders.Platform.Navigation.Policy;

namespace Cdp.ScriptableIde;

/// <summary>Maps Roslyn navigation wire → <c>navigation_scene/v1</c> via platform Navigation.Code.</summary>
internal static class NavigationSceneBridge
{
    internal static string RelatedSceneJson(
        string roslynWireJson,
        string? preset,
        int? maxRelated,
        IReadOnlyList<string>? includeKinds,
        IReadOnlyList<string>? excludeKinds)
    {
        var profile = NavigationProfile.FromExplore(preset, maxRelated, includeKinds, excludeKinds);
        var scene = NavigationCodeExplorer.ExploreRelatedFromWire(roslynWireJson, profile);
        return NavigationSceneJson.ToJson(scene);
    }
}
