using EmuDOS.Core.Infrastructure;
using EmuDOS.Core.Library;
using EmuDOS.Core.Model;

namespace EmuDOS.Tests;

public class LibraryDatabaseTests
{
    [Fact]
    public void Upsert_and_get_round_trip_with_promoted_fields()
    {
        var (db, paths, store) = NewDb();
        var box = MakeGamebox(paths, store, "Doom");

        var game = db.UpsertFromGamebox(box);

        Assert.Equal("Doom", game.Title);
        Assert.Equal("DOOM.EXE", game.Executable);
        Assert.Equal("Vga", game.Machine);
        Assert.Equal(box, game.GameboxPath); // portable relative storage round-trips to absolute
        Assert.Equal(box, Assert.Single(db.GetGames()).GameboxPath);
    }

    [Fact]
    public void Upsert_is_idempotent_and_preserves_stats()
    {
        var (db, paths, store) = NewDb();
        var box = MakeGamebox(paths, store, "Keen");

        var game = db.UpsertFromGamebox(box);
        db.RecordPlay(game.Id);
        var refreshed = db.UpsertFromGamebox(box);

        Assert.Equal(game.Id, refreshed.Id);
        Assert.Equal(1, refreshed.PlayCount); // stats not clobbered by refresh
        Assert.Single(db.GetGames());
    }

    [Fact]
    public void Sync_adds_present_and_prunes_missing_gameboxes()
    {
        var (db, paths, store) = NewDb();
        var a = MakeGamebox(paths, store, "A");
        db.SyncFromGameboxes();
        Assert.Single(db.GetGames());

        Directory.Delete(a, recursive: true);
        MakeGamebox(paths, store, "B");
        var count = db.SyncFromGameboxes();

        Assert.Equal(1, count);
        Assert.Equal("B", Assert.Single(db.GetGames()).Title);
    }

    [Fact]
    public void RecordPlay_increments_and_stamps_last_played()
    {
        var (db, paths, store) = NewDb();
        var game = db.UpsertFromGamebox(MakeGamebox(paths, store, "X"));

        db.RecordPlay(game.Id);
        db.RecordPlay(game.Id);

        var updated = Assert.Single(db.GetGames());
        Assert.Equal(2, updated.PlayCount);
        Assert.NotNull(updated.LastPlayed);
    }

    [Fact]
    public void Media_round_trips_with_portable_paths()
    {
        var (db, paths, store) = NewDb();
        var game = db.UpsertFromGamebox(MakeGamebox(paths, store, "M"));
        var art = Path.Combine(paths.DataRoot, "art", "front.png");

        db.AddMedia(game.Id, MediaKind.BoxFront, art, "ScreenScraper");

        var media = Assert.Single(db.GetMedia(game.Id));
        Assert.Equal(MediaKind.BoxFront, media.Kind);
        Assert.Equal(art, media.Path);
        Assert.Equal("ScreenScraper", media.Source);
    }

    [Fact]
    public void Library_is_rebuildable_from_gameboxes_after_db_loss()
    {
        var (db, paths, store) = NewDb();
        MakeGamebox(paths, store, "R1");
        MakeGamebox(paths, store, "R2");
        db.SyncFromGameboxes();
        Assert.Equal(2, db.GetGames().Count);

        // A brand-new (empty) DB recovers the full library by re-scanning gameboxes.
        var fresh = new LibraryDatabase(paths, store, Path.Combine(paths.DataRoot, "library2.db"));
        Assert.Empty(fresh.GetGames());
        fresh.SyncFromGameboxes();
        Assert.Equal(2, fresh.GetGames().Count);
    }

    private static (LibraryDatabase db, AppPaths paths, GameboxStore store) NewDb()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "emudos-tests", Guid.NewGuid().ToString("N")));
        var store = new GameboxStore();
        return (new LibraryDatabase(paths, store), paths, store);
    }

    private static string MakeGamebox(AppPaths paths, GameboxStore store, string title)
    {
        var box = Path.Combine(paths.GameboxesDir, title);
        store.WriteProfile(box, new GameProfile
        {
            Title = title,
            Machine = new MachineSpec { Machine = MachineType.Vga },
            Launch = new LaunchSpec { Executable = title.ToUpperInvariant() + ".EXE" },
        });
        return box;
    }
}
