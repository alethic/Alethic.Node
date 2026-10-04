using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.Tests;

/// <summary>
/// The memory load, as read on this machine and as made out of what Linux's files say.
/// </summary>
[TestClass]
public class MemoryLoadTests
{

    /// <summary>
    /// What the machine says is a fraction, or nothing, and reading it throws nothing, whatever the machine.
    /// </summary>
    [TestMethod]
    public void The_machine_s_load_is_a_fraction_or_nothing()
    {
        var load = MemoryLoad.Read();
        Assert.IsTrue(load is null or (>= 0 and <= 1), $"The memory load read {load}.");
    }

    /// <summary>
    /// Without a container limit, the load is the memory not available against the whole.
    /// </summary>
    [TestMethod]
    public void Without_a_limit_the_load_is_what_is_not_available()
    {
        const string meminfo = "MemTotal:       16000000 kB\nMemFree:         1000000 kB\nMemAvailable:    4000000 kB\n";
        Assert.AreEqual(0.75, MemoryLoad.FromLinux(meminfo, null, null));
        Assert.AreEqual(0.75, MemoryLoad.FromLinux(meminfo, "max", "123456"));
        Assert.AreEqual(0.75, MemoryLoad.FromLinux(meminfo, "9223372036854771712", "123456"));
    }

    /// <summary>
    /// Within a container limit, the load is the container's use against it.
    /// </summary>
    [TestMethod]
    public void Within_a_limit_the_load_is_the_use_against_it()
    {
        const string meminfo = "MemTotal:       16000000 kB\nMemAvailable:    4000000 kB\n";
        Assert.AreEqual(0.5, MemoryLoad.FromLinux(meminfo, "2147483648\n", "1073741824\n"));
        Assert.AreEqual(1, MemoryLoad.FromLinux(meminfo, "2147483648", "3000000000"));
    }

    /// <summary>
    /// With nothing to go on, there is no load.
    /// </summary>
    [TestMethod]
    public void With_nothing_to_go_on_there_is_no_load()
    {
        Assert.IsNull(MemoryLoad.FromLinux(null, null, null));
        Assert.IsNull(MemoryLoad.FromLinux("MemFree: 1 kB\n", "max", null));
    }

}
