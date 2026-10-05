using Microsoft.VisualStudio.TestTools.UnitTesting;
using NetParity.Core;

namespace NetParity.Tests;

[TestClass]
public sealed class SettingsStoreTests
{
    private string _path = null!;

    [TestInitialize]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), "netparity-tests", Guid.NewGuid().ToString("N"), "settings.json");
    }

    [TestCleanup]
    public void Teardown()
    {
        var directory = Path.GetDirectoryName(_path);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void MissingFileYieldsDefaults()
    {
        var settings = SettingsStore.Load(_path);

        Assert.AreEqual(0.94, settings.Opacity, 0.0001);
        Assert.AreEqual("1.1.1.1", settings.LatencyHost);
        Assert.IsTrue(settings.ShowLatency);
        Assert.AreEqual(-1d, settings.WindowLeft);
        Assert.AreEqual(SpeedUnit.Auto, settings.UnitMode);
    }

    [TestMethod]
    public void RoundTripPreservesEveryField()
    {
        var original = new AppSettings
        {
            WindowLeft = 320.5,
            WindowTop = 144.25,
            Opacity = 0.75,
            UnitMode = SpeedUnit.Bytes,
            LatencyHost = "8.8.8.8",
            LatencyPort = 8443,
            ShowLatency = false,
            RunAtStartup = true,
            Accent = "#FF00E5"
        };

        SettingsStore.Save(original, _path);
        var loaded = SettingsStore.Load(_path);

        Assert.AreEqual(original.WindowLeft, loaded.WindowLeft, 0.0001);
        Assert.AreEqual(original.WindowTop, loaded.WindowTop, 0.0001);
        Assert.AreEqual(original.Opacity, loaded.Opacity, 0.0001);
        Assert.AreEqual(original.UnitMode, loaded.UnitMode);
        Assert.AreEqual(original.LatencyHost, loaded.LatencyHost);
        Assert.AreEqual(original.LatencyPort, loaded.LatencyPort);
        Assert.AreEqual(original.ShowLatency, loaded.ShowLatency);
        Assert.AreEqual(original.RunAtStartup, loaded.RunAtStartup);
        Assert.AreEqual(original.Accent, loaded.Accent);
    }

    [TestMethod]
    public void CorruptFileFallsBackToDefaultsInsteadOfThrowing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ this is not json");

        var settings = SettingsStore.Load(_path);

        Assert.AreEqual("1.1.1.1", settings.LatencyHost);
    }

    [TestMethod]
    public void EmptyFileFallsBackToDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, string.Empty);

        var settings = SettingsStore.Load(_path);

        Assert.IsNotNull(settings);
    }

    [TestMethod]
    public void SaveCreatesMissingDirectories()
    {
        SettingsStore.Save(new AppSettings(), _path);

        Assert.IsTrue(File.Exists(_path));
    }

    [TestMethod]
    public void EnumsPersistByNameSoFilesStayReadable()
    {
        SettingsStore.Save(new AppSettings { UnitMode = SpeedUnit.Bytes }, _path);

        var json = File.ReadAllText(_path);

        StringAssert.Contains(json, "Bytes");
    }

    [TestMethod]
    public void SaveToUnwritablePathDoesNotThrow()
    {
        var bogus = "Z:\\definitely-not-a-drive\\nested\\settings.json";

        SettingsStore.Save(new AppSettings { Opacity = 0.5 }, bogus);
    }
}
