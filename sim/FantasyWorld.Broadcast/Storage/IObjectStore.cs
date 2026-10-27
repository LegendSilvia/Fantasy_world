namespace FantasyWorld.Broadcast.Storage;

/// <summary>
/// Where the broadcast keeps the world, its backups, the manifest and every episode: Cloudflare R2 in production, a
/// directory in tests and local dry runs. Keys use forward slashes (<c>backups/2026-10-27.fw.db</c>).
/// </summary>
public interface IObjectStore
{
    /// <summary>The object's bytes, or null when there is no such key.</summary>
    Task<byte[]?> GetAsync(string key, CancellationToken ct = default);

    Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default);

    /// <summary>Every key that starts with <paramref name="prefix"/>, in ordinal order.</summary>
    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>Every object under <paramref name="prefix"/> with its size in bytes, in ordinal order of key.</summary>
    async Task<IReadOnlyList<StoredObject>> ListSizesAsync(string prefix, CancellationToken ct = default)
    {
        var objects = new List<StoredObject>();
        foreach (var key in await ListAsync(prefix, ct).ConfigureAwait(false))
            objects.Add(new StoredObject(key, (await GetAsync(key, ct).ConfigureAwait(false))?.LongLength ?? 0));
        return objects;
    }
}

/// <summary>An object in a store and its size in bytes.</summary>
public sealed record StoredObject(string Key, long Bytes);

/// <summary>A view of a store under a key prefix: the live broadcast under <c>live/</c>, dry runs under <c>staging/</c>.</summary>
public sealed class PrefixedStore(IObjectStore inner, string prefix) : IObjectStore
{
    public string Prefix => prefix;

    public Task<byte[]?> GetAsync(string key, CancellationToken ct = default) => inner.GetAsync(prefix + key, ct);

    public Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default) =>
        inner.PutAsync(prefix + key, content, contentType, ct);

    public async Task<IReadOnlyList<string>> ListAsync(string keyPrefix, CancellationToken ct = default) =>
        (await inner.ListAsync(prefix + keyPrefix, ct).ConfigureAwait(false)).Select(k => k[prefix.Length..]).ToList();

    public Task DeleteAsync(string key, CancellationToken ct = default) => inner.DeleteAsync(prefix + key, ct);

    public async Task<IReadOnlyList<StoredObject>> ListSizesAsync(string keyPrefix, CancellationToken ct = default) =>
        (await inner.ListSizesAsync(prefix + keyPrefix, ct).ConfigureAwait(false)).Select(o => o with { Key = o.Key[prefix.Length..] }).ToList();
}
