namespace BlindSwordsman.Setup.Core;

public sealed record TransferProgress(long BytesReceived, long TotalBytes)
{
    public int Percent => TotalBytes <= 0
        ? 0
        : (int)Math.Clamp(BytesReceived * 100 / TotalBytes, 0, 100);
}

public sealed class ArtifactDownloader(HttpClient httpClient)
{
    private const long MaximumAssetSize = 2L * 1024 * 1024 * 1024;

    public async Task<string> DownloadAsync(
        ReleaseAssetDescriptor asset,
        string destinationDirectory,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (asset.Size <= 0 || asset.Size > MaximumAssetSize)
        {
            throw new InvalidDataException("Release asset size is outside the supported range.");
        }

        var directory = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(directory);
        var directoryInfo = new DirectoryInfo(directory);
        if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Refusing to download through a reparse-point staging directory.");
        }

        var finalPath = Path.Combine(directory, asset.Name);
        var partialPath = finalPath + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
            request.Headers.UserAgent.ParseAdd("Blind-Soldier-Setup/0.1");
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long contentLength && contentLength != asset.Size)
            {
                throw new InvalidDataException("Release asset length does not match the channel manifest.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (var target = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                long received = 0;
                while (true)
                {
                    var count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (count == 0)
                    {
                        break;
                    }

                    received = checked(received + count);
                    if (received > asset.Size)
                    {
                        throw new InvalidDataException("Release asset exceeded its declared length.");
                    }

                    await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    progress?.Report(new TransferProgress(received, asset.Size));
                }

                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (received != asset.Size)
                {
                    throw new InvalidDataException("Release asset ended before its declared length.");
                }
            }

            var actualHash = await HashVerifier.ComputeSha256Async(partialPath, cancellationToken).ConfigureAwait(false);
            if (!HashVerifier.FixedTimeEquals(asset.Sha256, actualHash))
            {
                throw new InvalidDataException("Release asset SHA-256 does not match the channel manifest.");
            }

            if (File.Exists(finalPath))
            {
                var existing = await HashVerifier.ComputeSha256Async(finalPath, cancellationToken).ConfigureAwait(false);
                if (!HashVerifier.FixedTimeEquals(asset.Sha256, existing))
                {
                    throw new IOException($"Refusing to overwrite a different existing staged asset: {finalPath}");
                }

                File.Delete(partialPath);
                return finalPath;
            }

            File.Move(partialPath, finalPath);
            progress?.Report(new TransferProgress(asset.Size, asset.Size));
            return finalPath;
        }
        finally
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
    }
}
