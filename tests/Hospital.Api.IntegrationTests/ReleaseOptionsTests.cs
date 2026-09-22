using Hospital.Api.Configuration;

namespace Hospital.Api.IntegrationTests;

public sealed class ReleaseOptionsTests
{
    [Fact]
    public void LocalRevisionIsAllowedOnlyAsANonDeployableRevision()
    {
        ReleaseOptions options = new() { Revision = "local" };

        Assert.True(ReleaseOptions.IsLocalOrGitRevision(options));
        Assert.False(ReleaseOptions.IsDeployableGitRevision(options));
    }

    [Theory]
    [InlineData("A36A65CA8F385261BC53E276309DA508981EA997")]
    [InlineData("a36a65c")]
    [InlineData("main")]
    [InlineData("")]
    public void InvalidDeploymentRevisionsAreRejected(string revision)
    {
        ReleaseOptions options = new() { Revision = revision };

        Assert.False(ReleaseOptions.IsLocalOrGitRevision(options));
        Assert.False(ReleaseOptions.IsDeployableGitRevision(options));
    }

    [Fact]
    public void FullLowercaseGitShaIsDeployable()
    {
        ReleaseOptions options = new()
        {
            Revision = "a36a65ca8f385261bc53e276309da508981ea997",
        };

        Assert.True(ReleaseOptions.IsLocalOrGitRevision(options));
        Assert.True(ReleaseOptions.IsDeployableGitRevision(options));
    }
}
