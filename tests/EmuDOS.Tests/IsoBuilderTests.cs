using System.Text;
using System.Threading;
using EmuDOS.Core.Import;

namespace EmuDOS.Tests;

public class IsoBuilderTests
{
    [Fact]
    public void BuildFromFolder_produces_a_readable_iso9660_image()
    {
        var src = Path.Combine(Path.GetTempPath(), "emudos_iso_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "READOUT.TXT"), "hello from EmuDOS");
        var iso = src + ".iso";

        try
        {
            // IMAPI2 is COM and wants an STA thread, mirroring how the app builds these.
            BuildOnSta(() => IsoBuilder.BuildFromFolder(src, iso, "TEST DISC"));

            Assert.True(File.Exists(iso));
            Assert.True(new FileInfo(iso).Length >= 16 * 2048);

            // ISO9660 primary volume descriptor: the magic "CD001" sits at sector 16, offset 1.
            using var fs = File.OpenRead(iso);
            fs.Seek(16 * 2048 + 1, SeekOrigin.Begin);
            var magic = new byte[5];
            fs.ReadExactly(magic);
            Assert.Equal("CD001", Encoding.ASCII.GetString(magic));
        }
        finally
        {
            try { Directory.Delete(src, recursive: true); } catch { /* best effort */ }
            try { File.Delete(iso); } catch { /* best effort */ }
        }
    }

    private static void BuildOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
#pragma warning disable CA1416 // EmuDOS is Windows-only; STA + IMAPI2 are Windows APIs.
        thread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
        thread.Start();
        thread.Join();
        if (error is not null)
            throw error;
    }
}
