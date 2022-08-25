namespace Chickensoft.Chicken {
  using System.Collections.Generic;
  using System.IO;
  using System.IO.Abstractions;
  using System.Threading.Tasks;
  using CliFx.Exceptions;

  public interface IAddonRepo {
    Task CacheAddon(RequiredAddon addon, Config config);
    Task DeleteAddon(RequiredAddon addon, Config config);
    Task CopyAddonFromCache(RequiredAddon addon, Config config);
    Task<Dictionary<string, string>> LoadCache(Config config);
  }

  public class AddonRepo : IAddonRepo {
    private readonly IApp _app;
    private readonly IFileSystem _fs;

    public AddonRepo(IApp app) {
      _app = app;
      _fs = app.FS;
    }

    /// <summary>
    /// Returns a dictionary of addons in the cache. Each key is the addon's
    /// url and each value is the directory in the cache containing a git clone
    /// of the addon.
    ///
    /// The cache is just a folder (typically `.addons` in a project folder)
    /// which contains git clones of addon repositories.
    /// </summary>
    /// <param name="config">Addon configuration containing paths.</param>
    /// <returns>Map of url's to addon cache directories.</returns>
    public async Task<Dictionary<string, string>> LoadCache(
      Config config
    ) {
      if (!_fs.Directory.Exists(config.CachePath)) {
        _fs.Directory.CreateDirectory(config.CachePath);
      }
      var urls = new Dictionary<string, string>();
      var directoriesInCachePath = _fs.Directory.GetDirectories(
        config.CachePath
      );
      foreach (var directory in directoriesInCachePath) {
        var shell = _app.CreateShell(directory);
        var result = await shell.Run("git", "remote", "get-url", "origin");
        var url = result.StandardOutput.Trim();
        urls.Add(url, directory);
      }
      return urls;
    }

    public async Task CacheAddon(RequiredAddon addon, Config config) {
      var addonCachePath = Path.Combine(config.CachePath, addon.Name);
      if (_fs.Directory.Exists(addonCachePath)) { return; }
      var cachePathShell = _app.CreateShell(config.CachePath);
      await cachePathShell.Run(
        "git", "clone", addon.Url, "--recurse-submodules", addon.Name
      );
    }

    public async Task DeleteAddon(RequiredAddon addon, Config config) {
      var addonPath = Path.Combine(config.AddonsPath, addon.Name);
      if (!_fs.Directory.Exists(addonPath)) { return; }
      var status = await _app.CreateShell(addonPath).RunUnchecked(
        "git", "status", "--porcelain"
      );
      if (status.Success) {
        // Installed addon is unmodified by the user, free to delete.
        await _app.CreateShell(config.AddonsPath).Run("rm", "-rf", addonPath);
      }
      else {
        throw new CommandException(
          $"Cannot delete modified addon {addon}. Please backup or discard " +
          "your changes and delete the addon manually." +
          "\n" + status.StandardOutput
        );
      }

    }

    public async Task CopyAddonFromCache(RequiredAddon addon, Config config) {
      // make sure correct branch is checked out in cache
      var addonCachePath = Path.Combine(config.CachePath, addon.Name);
      var addonCacheShell = _app.CreateShell(addonCachePath);
      await addonCacheShell.Run("git", "checkout", "-f", addon.Checkout);
      await addonCacheShell.RunUnchecked("git", "pull");
      await addonCacheShell.RunUnchecked(
        "git", "submodule", "update", "--init", "--recursive"
      );
      // copy addon from cache to installation location
      var workingShell = _app.CreateShell(config.ProjectPath);
      var copyFromPath = Path.Combine(config.CachePath, addon.Name);
      var subfolder = addon.Subfolder;
      if (subfolder != "/") {
        copyFromPath = Path.Combine(copyFromPath, subfolder);
      }
      copyFromPath = Path.TrimEndingDirectorySeparator(copyFromPath) +
        _fs.Path.DirectorySeparatorChar;
      var addonInstallPath = Path.Combine(config.AddonsPath, addon.Name);
      // copy files from addon cache to addon dir, excluding git folders.
      if (_fs.Path.DirectorySeparatorChar == '\\') {
        // if we're running on windows, use robocopy instead of rsync
        await workingShell.Run(
          "robocopy", copyFromPath, addonInstallPath, "/e", "/xd", ".git"
        );
      }
      else {
        await workingShell.Run(
          "rsync", "-av", copyFromPath, addonInstallPath, "--exclude", ".git"
        );
      }
      var addonShell = _app.CreateShell(addonInstallPath);
      // Make a junk repo in the installed addon dir. We use this for change
      // tracking to avoid deleting a modified addon.
      await addonShell.Run("git", "init");
      await addonShell.Run("git", "add", "-A");
      await addonShell.Run(
        "git", "commit", "-m", "Initial commit"
      );
    }
  }
}
