namespace Territory.Tests;

public static class TestDirectories
{
    private const int DeleteAttempts = 10;

    /// <summary>Scanners may briefly keep a handle on a just-closed file: retries before giving up.</summary>
    public static async Task DeleteAsync(string path)
    {
        for (var attempt = 1; Directory.Exists(path); attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException) when (attempt < DeleteAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));
            }
        }
    }
}
