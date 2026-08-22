using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetBuildTest.Core;

public static class BuildTestJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
