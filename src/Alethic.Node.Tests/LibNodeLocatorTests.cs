using System;
using System.Runtime.InteropServices;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.Tests;

/// <summary>
/// The library an engine starts from: one older than node-api-dotnet's embedding calls are written for is refused,
/// where its version can be read, rather than left to end the process.
/// </summary>
[TestClass]
public class LibNodeLocatorTests
{

    /// <summary>
    /// The library the tests carry, found where node-api-dotnet finds it, is new enough.
    /// </summary>
    [TestMethod]
    public void The_library_the_packages_deploy_is_accepted()
    {
        LibNodeLocator.Verify(LibNodeLocator.Locate(null));
    }

    /// <summary>
    /// A library whose version resource says it is older is refused, naming the file and the version it needs. Any
    /// Windows library will do to show it: the runtime's own, whose major version is far below Node's.
    /// </summary>
    [TestMethod]
    public void An_older_library_is_refused_where_its_version_can_be_read()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) == false)
            return;

        var older = typeof(object).Assembly.Location;
        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => LibNodeLocator.Verify(older));
        StringAssert.Contains(thrown.Message, older);
        StringAssert.Contains(thrown.Message, $"LibNode {LibNodeLocator.MinimumMajorVersion}");
    }

}
