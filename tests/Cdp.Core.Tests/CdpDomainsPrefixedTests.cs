using Xunit;

namespace Cdp.Core.Tests;

public sealed class CdpDomainsPrefixedTests
{
    [Theory]
    [InlineData(CdpDomains.CodebaseIndex, "codebase_index_search", "codebase_index_search")]
    [InlineData(CdpDomains.Debug, "debug_launch", "debug_launch")]
    [InlineData(CdpDomains.Git, "git_scene", "git_scene")]
    [InlineData(CdpDomains.MemoryWorld, "knowledge_tags", "memory_world_knowledge_tags")]
    public void Prefixed_collapses_domain_prefixed_underlyings(string domain, string underlying, string expected)
        => Assert.Equal(expected, CdpDomains.Prefixed(domain, underlying));

    [Theory]
    [InlineData(CdpDomains.CodebaseIndex, "search", "codebase_index_search")]
    [InlineData(CdpDomains.CodebaseIndex, "codebase_index_search", "codebase_index_search")]
    [InlineData(CdpDomains.Git, "scene", "git_scene")]
    [InlineData(CdpDomains.MemoryWorld, "knowledge_tags", "knowledge_tags")]
    public void ExpandUnderlying_restores_catalog_ids(string domain, string underlying, string expected)
        => Assert.Equal(expected, CdpDomains.ExpandUnderlying(domain, underlying));

    [Fact]
    public void TrySplit_single_prefix_wire_name()
    {
        Assert.True(CdpDomains.TrySplit("codebase_index_search", out var domain, out var underlying));
        Assert.Equal(CdpDomains.CodebaseIndex, domain);
        Assert.Equal("search", underlying);
        Assert.Equal("codebase_index_search", CdpDomains.ExpandUnderlying(domain, underlying));
    }
}
