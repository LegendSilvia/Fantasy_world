namespace FantasyWorld.Broadcast.Storage;

/// <summary>An object store in a local directory, for tests and for trying the daily run without R2.</summary>
public sealed class LocalObjectStore(string root) : IObjectStore
{
    private string PathOf(string key)
    {
        if (key.Length == 0 || key.Split('/').Any(part => part is "" or "." or ".."))
            throw new ArgumentException($"Not a valid object key: '{key}'.");
        return Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
    {
        var path = PathOf(key);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false) : null;
    }

    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default)
    {
        IReadOnlyList<string> keys = !Directory.Exists(root)
            ? []
            : Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
                .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToList();
        return Task.FromResult(keys);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        File.Delete(PathOf(key));
        return Task.CompletedTask;
    }
}
