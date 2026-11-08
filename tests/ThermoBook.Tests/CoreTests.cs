using ThermoBook.Services;

namespace ThermoBook.Tests;

public class GpuAggregatorTests
{
    private const string Luid = "luid_0x00000000_0x00012ADC_phys_0";

    [Fact]
    public void Sums_processes_on_the_same_engine_and_takes_max_per_type()
    {
        var usage = GpuAggregator.Aggregate(
        [
            ($"pid_100_{Luid}_eng_0_engtype_3D", 10),
            ($"pid_200_{Luid}_eng_0_engtype_3D", 15),   // mesmo motor: soma 25
            ($"pid_300_{Luid}_eng_1_engtype_3D", 20),   // outro motor 3D: 20
            ($"pid_400_{Luid}_eng_2_engtype_VideoDecode", 60),
            ($"pid_400_{Luid}_eng_3_engtype_VideoProcessing", 30),
        ]);

        Assert.Equal(25, usage.ThreeD);
        Assert.Equal(60, usage.Video);
        Assert.Equal(60, usage.All);
    }

    [Fact]
    public void Caps_at_100_and_ignores_unknown_names()
    {
        var usage = GpuAggregator.Aggregate(
        [
            ($"pid_1_{Luid}_eng_0_engtype_3D", 80),
            ($"pid_2_{Luid}_eng_0_engtype_3D", 70),
            ("_Total", 999),
        ]);

        Assert.Equal(100, usage.All);
        Assert.Equal(0, usage.Video);
    }

    [Fact]
    public void Empty_input_is_zero()
    {
        Assert.Equal(new GpuAggregator.GpuUsage(0, 0, 0), GpuAggregator.Aggregate([]));
    }
}

public class PowerProfileTests
{
    [Theory]
    [InlineData(0u, 100u, "Frio")]
    [InlineData(0u, 80u, "Silencioso")]
    [InlineData(3u, 100u, "Normal")]
    [InlineData(2u, 100u, "Máximo")]
    public void Matches_known_profiles(uint turbo, uint limit, string name)
    {
        Assert.Equal(name, PowerService.Match(turbo, limit)?.Name);
    }

    [Fact]
    public void Custom_combination_has_no_profile()
    {
        Assert.Null(PowerService.Match(1, 90));
    }

    [Fact]
    public void Profiles_are_unique()
    {
        var keys = PowerService.Profiles.Select(p => (p.Turbo, p.Limit)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}

public class SettingsStoreTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "thermobook-tests", Guid.NewGuid() + ".json");

    [Fact]
    public void Round_trip()
    {
        var store = new SettingsStore(TempFile());
        store.Save(new Settings(Profile: "Frio"));
        Assert.Equal("Frio", store.Load().Profile);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        Assert.Null(new SettingsStore(TempFile()).Load().Profile);
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ isto nao e json");
        Assert.Null(new SettingsStore(path).Load().Profile);
    }
}

public class TrayIconRendererTests
{
    [Theory]
    [InlineData(55, 90, 210, 120)]
    [InlineData(80, 255, 165, 60)]
    [InlineData(95, 255, 85, 85)]
    public void Color_by_temperature(double celsius, int r, int g, int b)
    {
        var c = TrayIconRenderer.TempColor(celsius);
        Assert.Equal((r, g, b), (c.R, c.G, c.B));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(54.0)]
    [InlineData(105.0)]
    public void Renders_icon_of_requested_size(double? celsius)
    {
        using var icon = TrayIconRenderer.Render(celsius, 24);
        Assert.Equal(24, icon.Width);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void Both_digits_are_drawn_without_wrapping(int size)
    {
        // Regressão: o texto quebrava de linha e só o primeiro dígito aparecia.
        using var icon = TrayIconRenderer.Render(57, size);
        using var bmp = icon.ToBitmap();
        int Colored(int x0, int x1)
        {
            int n = 0;
            for (int x = x0; x < x1; x++)
                for (int y = 0; y < size; y++)
                    if (bmp.GetPixel(x, y).G > 120) n++;
            return n;
        }
        Assert.True(Colored(0, size / 4) > 0, "primeiro dígito ausente");
        Assert.True(Colored(size * 3 / 4, size) > 0, "segundo dígito ausente");
    }
}

public class SensorServiceTests
{
    [Fact]
    public void Reads_real_counters_from_this_machine()
    {
        var s = SensorService.Read();
        Assert.True(s.RamTotal > 0);
        Assert.InRange(s.CpuUse, 0, 100);
        Assert.InRange(s.DiskActive, 0, 100);
    }
}
