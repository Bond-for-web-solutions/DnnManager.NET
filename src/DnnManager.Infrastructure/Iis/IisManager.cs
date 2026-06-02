using System.Security.AccessControl;
using System.Security.Principal;
using DnnManager.Application.Abstractions;
using DnnManager.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Web.Administration;

namespace DnnManager.Infrastructure.Iis;

public sealed class IisManager : IIisManager
{
    private readonly ILogger<IisManager> _log;

    public IisManager(ILogger<IisManager> log) => _log = log;

    public Result CreateSite(string siteName, string physicalPath, string hostname, int port)
    {
        try
        {
            using var sm = new ServerManager();
            // remove if exists
            var existingPool = sm.ApplicationPools[siteName];
            if (existingPool != null) sm.ApplicationPools.Remove(existingPool);
            var existingSite = sm.Sites[siteName];
            if (existingSite != null) sm.Sites.Remove(existingSite);

            var pool = sm.ApplicationPools.Add(siteName);
            pool.ManagedRuntimeVersion = "v4.0";
            pool.ManagedPipelineMode = ManagedPipelineMode.Integrated;

            var site = sm.Sites.Add(siteName, "http", $"*:{port}:{hostname}", physicalPath);
            site.ApplicationDefaults.ApplicationPoolName = siteName;
            sm.CommitChanges();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "IIS CreateSite failed");
            return Result.Fail(ex.Message);
        }
    }

    public Result RemoveSite(string siteName)
    {
        try
        {
            using var sm = new ServerManager();
            var site = sm.Sites[siteName];
            if (site != null)
            {
                try { site.Stop(); } catch { /* ignore */ }
                sm.Sites.Remove(site);
            }
            var pool = sm.ApplicationPools[siteName];
            if (pool != null)
            {
                try { pool.Stop(); } catch { /* ignore */ }
                sm.ApplicationPools.Remove(pool);
            }
            sm.CommitChanges();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "IIS RemoveSite failed");
            return Result.Fail(ex.Message);
        }
    }

    public Result StartSite(string siteName)
    {
        try
        {
            using var sm = new ServerManager();
            var site = sm.Sites[siteName];
            if (site is null) return Result.Fail($"Site '{siteName}' not found");
            site.Start();
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public bool SiteExists(string siteName)
    {
        try { using var sm = new ServerManager(); return sm.Sites[siteName] != null; }
        catch { return false; }
    }

    public string? GetSiteState(string siteName)
    {
        try
        {
            using var sm = new ServerManager();
            var s = sm.Sites[siteName];
            return s?.State.ToString();
        }
        catch { return null; }
    }

    public Result GrantPermissions(string path, IEnumerable<string> identities)
    {
        try
        {
            var di = new DirectoryInfo(path);
            var sec = di.GetAccessControl();
            foreach (var id in identities)
            {
                try
                {
                    var rule = new FileSystemAccessRule(
                        ResolveIdentity(id),
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow);
                    sec.AddAccessRule(rule);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Skip identity {Identity}", id);
                }
            }
            di.SetAccessControl(sec);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    // "IIS APPPOOL\<name>" virtual accounts often can't be name-translated right after the
    // pool is created. Compute the deterministic app-pool SID instead so the grant always works.
    private static IdentityReference ResolveIdentity(string id)
    {
        const string prefix = "IIS APPPOOL\\";
        if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return AppPoolSid(id[prefix.Length..]);
        return new NTAccount(id);
    }

    // IIS derives an application-pool SID as S-1-5-82-{five little-endian uint32s of
    // SHA1(lowercased pool name encoded as UTF-16LE)}.
    private static SecurityIdentifier AppPoolSid(string appPoolName)
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes(appPoolName.ToLowerInvariant());
        var hash = System.Security.Cryptography.SHA1.HashData(bytes);
        var sb = new System.Text.StringBuilder("S-1-5-82");
        for (var i = 0; i < 5; i++)
            sb.Append('-').Append(BitConverter.ToUInt32(hash, i * 4));
        return new SecurityIdentifier(sb.ToString());
    }
}
