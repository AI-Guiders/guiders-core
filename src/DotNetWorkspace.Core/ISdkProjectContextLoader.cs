namespace DotNetWorkspace.Core;

public interface ISdkProjectContextLoader
{
    SdkProjectContext Load(string projectPath, ProjectContextLoadOptions? options = null);

    void Warm(string projectPath, ProjectContextLoadOptions? options = null);

    void Invalidate(string? projectPath = null);
}
