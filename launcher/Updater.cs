using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;

namespace CdmpLauncher
{
    // "Verificar atualização": the latest release of the mod's GitHub repository (Repo below). The release
    // carries either cdmp.asi or a .zip with cdmp.asi (and optionally winmm.dll and the cdmp.host/guest.ini).
    public static class Updater
    {
        // The mod's GitHub repository ("owner/repo"). Empty until it is published: the button only says so.
        public const string Repo = "";

        [DataContract] class Asset { [DataMember(Name = "name")] public string Name = ""; [DataMember(Name = "browser_download_url")] public string Url = ""; }
        [DataContract] class Release { [DataMember(Name = "tag_name")] public string Tag = ""; [DataMember(Name = "name")] public string Title = ""; [DataMember(Name = "assets")] public List<Asset> Assets = new List<Asset>(); }

        public class Found
        {
            public string Tag, Dir;            // Dir: extracted files (cdmp.asi, maybe winmm.dll / ini)
            public string AsiPath => Path.Combine(Dir, "cdmp.asi");
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var w = new WebClient();
            w.Headers[HttpRequestHeader.UserAgent] = "PywelTogether";
            return w;
        }

        public static bool Configured => Repo.Contains("/");

        // Downloads the latest release's files into a temp folder. Null when the release has no usable file.
        public static async Task<Found> FetchLatest(string repo)
        {
            repo = repo.Trim().Trim('/');
            if (repo.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) repo = repo.Substring(19);
            Release r;
            using (var w = Client())
            {
                var json = await w.DownloadDataTaskAsync("https://api.github.com/repos/" + repo + "/releases/latest");
                using (var ms = new MemoryStream(json)) r = (Release)new DataContractJsonSerializer(typeof(Release)).ReadObject(ms);
            }
            var asset = r.Assets.FirstOrDefault(a => a.Name.Equals("cdmp.asi", StringComparison.OrdinalIgnoreCase))
                     ?? r.Assets.FirstOrDefault(a => a.Name.EndsWith(".asi", StringComparison.OrdinalIgnoreCase))
                     ?? r.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (asset == null) return null;
            var dir = Path.Combine(Path.GetTempPath(), "cdmp_update_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, asset.Name);
            using (var w = Client()) await w.DownloadFileTaskAsync(asset.Url, file);
            if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var x = Path.Combine(dir, "x");
                ZipFile.ExtractToDirectory(file, x);
                foreach (var want in new[] { "cdmp.asi", "winmm.dll", "cdmp.host.ini", "cdmp.guest.ini" })
                {
                    var hit = Directory.GetFiles(x, want, SearchOption.AllDirectories).FirstOrDefault();
                    if (hit != null) File.Copy(hit, Path.Combine(dir, want), true);
                }
                if (!File.Exists(Path.Combine(dir, "cdmp.asi")))
                {
                    var any = Directory.GetFiles(x, "*.asi", SearchOption.AllDirectories).FirstOrDefault();
                    if (any == null) return null;
                    File.Copy(any, Path.Combine(dir, "cdmp.asi"), true);
                }
            }
            else if (!asset.Name.Equals("cdmp.asi", StringComparison.OrdinalIgnoreCase)) File.Move(file, Path.Combine(dir, "cdmp.asi"));
            return new Found { Tag = string.IsNullOrEmpty(r.Tag) ? r.Title : r.Tag, Dir = dir };
        }
    }
}
