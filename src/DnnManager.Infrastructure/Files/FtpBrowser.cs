using DnnManager.Application.Abstractions;
using DnnManager.Domain;
using FluentFTP;

namespace DnnManager.Infrastructure.Files;

public sealed class FtpBrowser : IFtpBrowser
{
    public async Task<Result<IReadOnlyList<string>>> ListDirectoriesAsync(
        string host, int port, string user, string password, string remotePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host)) return Result<IReadOnlyList<string>>.Fail("FTP host is empty.");
        try
        {
            using var client = new AsyncFtpClient(host, user ?? "", password ?? "", port <= 0 ? 21 : port);
            // Azure App Service (and most modern hosts) require FTPS. Auto negotiates
            // explicit TLS and falls back to plain FTP when the server allows it.
            client.Config.EncryptionMode = FtpEncryptionMode.Auto;
            client.Config.ValidateAnyCertificate = true;
            await client.AutoConnect(ct);
            var path = string.IsNullOrWhiteSpace(remotePath) ? "/" : remotePath;
            var items = await client.GetListing(path, token: ct);
            var dirs = items
                .Where(i => i.Type == FtpObjectType.Directory)
                .Select(i => i.Name)
                .Where(n => n != "." && n != "..")
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Result<IReadOnlyList<string>>.Ok(dirs);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<string>>.Fail($"FTP error: {ex.Message}");
        }
    }
}
