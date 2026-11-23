using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CdmpLauncher
{
    // A mod version the launcher can deploy: one cdmp.asi plus (optionally) its own ini templates.
    [DataContract]
    public class Profile
    {
        [DataMember] public string Name = "";
        [DataMember] public string AsiPath = "";      // source file (project build, a file the user picked, or the cache)
        [DataMember] public string AsiSha = "";       // sha256 of the asi when it was added (the cache key)
        [DataMember] public string ExpectedBuild = ""; // game build the asi was made for ("3663"); empty = don't check
        [DataMember] public bool Legacy;              // old mod without CDMP_ROLE: reads cdmp.ini / writes cdmp.log
        [DataMember] public string HostIni = "";      // optional template; empty = the one in config\mod
        [DataMember] public string GuestIni = "";

        public override string ToString() => Name;
    }

    [DataContract]
    public class Config
    {
        [DataMember] public string GameDir = "";
        [DataMember] public string ProjectDir = "";
        [DataMember] public string HostName = "Host";
        [DataMember] public string GuestName = "Guest";
        [DataMember] public string SessionCode = "";
        [DataMember] public string ManualHostIp = "";
        [DataMember] public string Transport = "steam";   // "steam" (padrÃ£o, como o mod original) | "udp" (rede local/VPN, opcional)
        [DataMember] public string FriendSteamId = "";    // SteamID64 do amigo (opcional; vazio = o mod acha o amigo que estÃ¡ jogando)
        [DataMember] public bool AutoE = true;
        [DataMember] public bool RecordPackets = false;
        [DataMember] public bool AutoSyncFromHost = true;
        [DataMember] public bool AutoRestoreWatch = true;
        [DataMember] public bool ShareSave = false;
        [DataMember] public string ShareSaveDir = "";
        [DataMember] public int PeerPort = 37015;
        [DataMember] public int DiscoveryPort = 37020;
        [DataMember] public int FilePort = 37021;
        [DataMember] public int FirstEDelaySec = 20;
        [DataMember] public int EIntervalSec = 30;
        [DataMember] public int MaxEPresses = 10;
        [DataMember] public int KeepLogs = 6;
        [DataMember] public string SelectedProfile = "";
        [DataMember] public List<Profile> Profiles = new List<Profile>();
        [DataMember] public string WinmmSha = "";
        [DataMember] public string DeployedAsiSha = "";
        [DataMember] public string LastExeStamp = "";
        [DataMember] public bool ModDisabled;
        [DataMember] public string PackageAsiSha = "";    // config\mod\cdmp.asi last installed into the game
        [DataMember] public string PackageIniSha = "";    // config\mod ini files last pushed into the game
        [DataMember] public string Language = "";   // "pt" | "en"; empty = ask at start

        public static string ExeDir => AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        public static string DataDir =>
            Environment.GetEnvironmentVariable("CDMP_LAUNCHER_DATA") is string d && d != "" ? d : Path.Combine(ExeDir, "config");
        public static string FilePath => Path.Combine(DataDir, "settings.json");

        public bool UseSteam => Transport != "udp";

        public Profile Current =>
            Profiles.FirstOrDefault(p => p.Name == SelectedProfile) ?? Profiles.LastOrDefault();

        public static Config Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    using (var fs = File.OpenRead(FilePath))
                    {
                        var c = (Config)new DataContractJsonSerializer(typeof(Config)).ReadObject(fs);
                        if (c.Profiles == null) c.Profiles = new List<Profile>();
                        return c;
                    }
                }
            }
            catch (Exception e)
            {
                try { File.Copy(FilePath, FilePath + ".quebrado", true); } catch { }
                AppLog.Write(Lang.T("settings.json ilegível, usando padrão: ") + e.Message);
            }
            return new Config();
        }

        public void Save()
        {
            Directory.CreateDirectory(DataDir);
            var tmp = FilePath + ".tmp";
            using (var ms = new MemoryStream())
            {
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true))
                    new DataContractJsonSerializer(typeof(Config)).WriteObject(w, this);
                File.WriteAllBytes(tmp, ms.ToArray());
            }
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
    }

    public static class AppLog
    {
        static readonly object Mu = new object();
        public static event Action<string> Line;

        public static void Write(string msg)
        {
            var l = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
            lock (Mu)
            {
                try
                {
                    Directory.CreateDirectory(Config.DataDir);
                    var p = Path.Combine(Config.DataDir, "launcher.log");
                    if (File.Exists(p) && new FileInfo(p).Length > 4 * 1024 * 1024)
                        File.Copy(p, p + ".1", true);
                    if (File.Exists(p + ".1") && File.Exists(p) && new FileInfo(p).Length > 4 * 1024 * 1024)
                        File.Delete(p);
                    File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd ") + l + "\r\n");
                }
                catch { }
            }
            Line?.Invoke(l);
        }
    }
}
