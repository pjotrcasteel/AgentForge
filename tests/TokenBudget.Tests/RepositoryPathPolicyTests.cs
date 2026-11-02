using Microsoft.VisualStudio.TestTools.UnitTesting;
using TokenBudget.Application;

namespace TokenBudget.Tests;

[TestClass]
public sealed class RepositoryPathPolicyTests
{
    [TestMethod]
    public void NoRepositoryRequested_DoesNotRequireAllowlist()
    {
        Assert.IsNull(RepositoryPathPolicy.Resolve(null, null));
    }

    [TestMethod]
    public void RequestedRepository_WithoutAllowlist_IsDenied()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => RepositoryPathPolicy.Resolve(".", null));
    }

    [TestMethod]
    public void RepositoryOutsideRoot_IsDenied()
    {
        string root = Path.Combine(Path.GetTempPath(), $"tokenbudget-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => RepositoryPathPolicy.Resolve("../outside", root));
        }
        finally
        {
            Directory.Delete(root);
        }
    }

    [TestMethod]
    public void RepositoryInsideRoot_IsAccepted()
    {
        string root = Path.Combine(Path.GetTempPath(), $"tokenbudget-root-{Guid.NewGuid():N}");
        string inside = Path.Combine(root, "repo");
        Directory.CreateDirectory(inside);
        try
        {
            Assert.AreEqual(inside, RepositoryPathPolicy.Resolve("repo", root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void MissingRepository_ReturnsDirectoryNotFound()
    {
        string root = Path.Combine(Path.GetTempPath(), $"tokenbudget-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Assert.ThrowsExactly<DirectoryNotFoundException>(() => RepositoryPathPolicy.Resolve("missing", root));
        }
        finally
        {
            Directory.Delete(root);
        }
    }
}
