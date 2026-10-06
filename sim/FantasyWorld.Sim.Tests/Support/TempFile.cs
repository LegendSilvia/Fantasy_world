namespace FantasyWorld.Sim.Tests.Support;

public sealed class TempFile : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fw-test-{Guid.NewGuid():N}.fw.db");

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", ".bak1", ".bak2", ".bak3" })
            if (File.Exists(Path + suffix)) File.Delete(Path + suffix);
    }
}

/// <summary>A directory removed with everything in it when the test ends.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fw-test-{Guid.NewGuid():N}")).FullName;

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
