namespace DotNetBuildTest.Core.Tests;

public class BuildTestResultDetailTests
{
    [Fact]
    public void Norm_defaults_to_auto()
    {
        Assert.Equal(BuildTestResultDetail.Auto, BuildTestResultDetail.Norm(null, includeRawOutput: false));
        Assert.Equal(BuildTestResultDetail.Full, BuildTestResultDetail.Norm(null, includeRawOutput: true));
        Assert.Equal(BuildTestResultDetail.Pulse, BuildTestResultDetail.Norm("pulse", false));
        Assert.Equal(BuildTestResultDetail.Slim, BuildTestResultDetail.Norm("fail", false));
    }

    [Fact]
    public void Effective_auto_is_pulse_on_green_slim_on_fail()
    {
        Assert.Equal(BuildTestResultDetail.Pulse, BuildTestResultDetail.Effective(BuildTestResultDetail.Auto, success: true));
        Assert.Equal(BuildTestResultDetail.Slim, BuildTestResultDetail.Effective(BuildTestResultDetail.Auto, success: false));
        Assert.Equal(BuildTestResultDetail.Slim, BuildTestResultDetail.Effective(BuildTestResultDetail.Slim, success: true));
    }
}
