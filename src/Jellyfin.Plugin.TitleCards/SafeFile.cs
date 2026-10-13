namespace Jellyfin.Plugin.TitleCards;

/// <summary>
/// Writes files so readers never see a half-written one: write a temporary file, then replace.
/// </summary>
/// <remarks>
/// Windows refuses to replace a file another process has open (a client downloading a card, an
/// antivirus scan of the file just written), so the replace is retried briefly before giving up.
/// </remarks>
public static class SafeFile
{
    private const int Attempts = 10;

    /// <summary>
    /// Writes text to a file through a temporary file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="contents">The text.</param>
    public static void WriteAllText(string path, string contents)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, contents);
        Replace(temporary, path, attempt => Thread.Sleep(Delay(attempt)));
    }

    /// <summary>
    /// Writes bytes to a file through a temporary file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="bytes">The bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public static async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
        for (var attempt = 1; ; attempt++)
        {
            if (TryReplace(temporary, path, attempt))
            {
                return;
            }

            await Task.Delay(Delay(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void Replace(string temporary, string path, Action<int> wait)
    {
        for (var attempt = 1; !TryReplace(temporary, path, attempt); attempt++)
        {
            wait(attempt);
        }
    }

    private static bool TryReplace(string temporary, string path, int attempt)
    {
        try
        {
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < Attempts)
        {
            return false;
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    private static TimeSpan Delay(int attempt) => TimeSpan.FromMilliseconds(100 * attempt);
}
