using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace CdmpLauncher
{
    public enum Role { Host, Guest }

    // Everything that touches files: the copies kept by the launcher (config\cache), profiles, the restore of
    // winmm.dll / cdmp.asi / ini, the save import, log collection and cleanup.
    public class Deployer
    {
        readonly Config cfg;
        public Deployer(Config c) { cfg = c; }

        // config\server: the co-op mod itself (cdmp.asi, winmm.dll, ini, version.txt) and its copies (cache, backups)
        public static string TemplateDir => Path.Combine(Config.DataDir, "server");
        public static string CacheDir => Path.Combine(TemplateDir, "cache");
        public static string BackupDir => Path.Combine(Config.DataDir, "backups");
        public string Bin => cfg.GameDir;

        public static string RoleName(Role r) => r == Role.Host ? "host" : "guest";

        // ---------- cache (content addressed: a copy Windows/antivirus cannot take from the game folder)

        public static string CachePath(string sha, string ext) => Path.Combine(CacheDir, sha + ext);

        public static string CacheStore(string src, string ext)
        {
            var sha = Util.Sha256(src);
            var dst = CachePath(sha, ext);
            if (!File.Exists(dst)) Util.CopyFile(src, dst);
            return sha;
        }

        // ---------- profiles

        // cache=true: keep our own copy now (downloads, the build found in the game); else only when deployed.
        public Profile AddProfile(string asiPath, string name = null, bool cache = false)
        {
            var sha = cache ? CacheStore(asiPath, ".asi") : Util.Sha256(asiPath);
            var existing = cfg.Profiles.FirstOrDefault(p => p.AsiSha == sha);
            if (existing != null) return existing;
            var file = Path.GetFileNameWithoutExtension(asiPath);
            var p2 = new Profile
            {
                Name = name ?? NiceName(file),
                AsiPath = asiPath,
                AsiSha = sha,
            };
            var m = Regex.Match(file, @"_(2663|2976|3460|3576|3663|\d{4})(_|$)");
            if (m.Success) p2.ExpectedBuild = m.Groups[1].Value;
            var n = p2.Name; int k = 2;
            while (cfg.Profiles.Any(x => x.Name == p2.Name)) p2.Name = n + " (" + k++ + ")";
            cfg.Profiles.Add(p2);
            return p2;
        }

        static string NiceName(string file)
        {
            var m = Regex.Match(file, @"cdmp_ns_(\d+)_r(\d+)");
            if (m.Success) return "Co-op mundo do host r" + m.Groups[2].Value + " (" + m.Groups[1].Value + ")";
            m = Regex.Match(file, @"cdmp_(\d+)_(v\d+)");
            if (m.Success) return "Mundos separados " + m.Groups[2].Value + " (" + m.Groups[1].Value + ")";
            return file;
        }

        // Every build in the project folder (cdmp_ns_*_rNN.asi, cdmp_*_vN.asi), sorted.
        public int ImportProjectBuilds()
        {
            if (string.IsNullOrEmpty(cfg.ProjectDir) || !Directory.Exists(cfg.ProjectDir)) return 0;
            int before = cfg.Profiles.Count;
            // the newest 5 co-op builds (cdmp_ns_*_rNN); the newest one is always the selected one
            int Num(string f) => int.Parse(Regex.Match(Path.GetFileName(f), @"_r(\d+)\.asi$").Groups[1].Value);
            var ns = Directory.GetFiles(cfg.ProjectDir, "cdmp_ns_*_r*.asi").Where(f => Regex.IsMatch(Path.GetFileName(f), @"^cdmp_ns_\d+_r\d+\.asi$")).OrderBy(Num).ToList();
            var files = ns.Skip(Math.Max(0, ns.Count - 5));
            foreach (var f in files)
            {
                try { AddProfile(f); } catch (Exception e) { AppLog.Write(Lang.T("não importei ") + f + ": " + e.Message); }
            }
            // old builds and leftovers of the separate-worlds mod are dropped from the list
            cfg.Profiles.RemoveAll(p => Regex.IsMatch(p.Name, "^Mundos separados") || p.Legacy);
            var newest = files.LastOrDefault();
            if (newest != null) { var np = ProfileBySha(Util.Sha256(newest)); if (np != null) cfg.SelectedProfile = np.Name; }
            return Math.Max(0, cfg.Profiles.Count - before);
        }

        public string AsiSource(Profile p)
        {
            if (!string.IsNullOrEmpty(p.AsiSha) && File.Exists(CachePath(p.AsiSha, ".asi"))) return CachePath(p.AsiSha, ".asi");
            if (File.Exists(p.AsiPath))
            {
                p.AsiSha = CacheStore(p.AsiPath, ".asi");
                return CachePath(p.AsiSha, ".asi");
            }
            return null;
        }

        public Profile ProfileBySha(string sha) => cfg.Profiles.FirstOrDefault(p => p.AsiSha == sha);

        // ---------- templates

        public string TemplatePath(Profile p, Role r)
        {
            var own = r == Role.Host ? p?.HostIni : p?.GuestIni;
            if (!string.IsNullOrEmpty(own) && File.Exists(own)) return own;
            return Path.Combine(TemplateDir, "cdmp." + RoleName(r) + ".ini");
        }

        // Which files the mod uses (it decides by CDMP_ROLE, see GameRun.Start):
        //   host   CDMP_ROLE=host   cdmp.host.ini   cdmp.host.log
        //   guest  no CDMP_ROLE     cdmp.ini        cdmp.log      (always on its own PC)
        // (server\cdmp.guest.ini is the guest template; in the game it becomes cdmp.ini)
        public string IniName(Profile p, Role r) => r == Role.Host ? "cdmp.host.ini" : "cdmp.ini";

        public string LogAlias(Profile p, Role r) => Path.Combine(Bin, r == Role.Host ? "cdmp.host.log" : "cdmp.log");

        public string EnvRole(Role r) => r == Role.Host ? "host" : null;

        // A co-op (session2) ini; an older mod's cdmp.ini (transport=steam, no [session2]) is not one.
        public static bool IsCoopIni(string path)
        {
            try { return File.Exists(path) && Ini.Read(path).Any(l => l.Trim().Equals("[session2]", StringComparison.OrdinalIgnoreCase)); }
            catch { return false; }
        }

        // First start: what is in the game folder becomes the launcher's copies.
        public void CaptureFromGame()
        {
            if (!Directory.Exists(Bin)) return;
            Directory.CreateDirectory(TemplateDir);
            var w = Path.Combine(Bin, "winmm.dll");
            if (File.Exists(w) && string.IsNullOrEmpty(cfg.WinmmSha)) { cfg.WinmmSha = CacheStore(w, ".dll"); AppLog.Write(Lang.T("cópia do winmm.dll guardada")); }
            foreach (Role r in new[] { Role.Host, Role.Guest })
            {
                var t = Path.Combine(TemplateDir, "cdmp." + RoleName(r) + ".ini");
                if (File.Exists(t)) continue;
                var own = Path.Combine(Bin, "cdmp." + RoleName(r) + ".ini");
                var baseIni = Path.Combine(Bin, "cdmp.ini");
                if (File.Exists(own)) { File.Copy(own, t); AppLog.Write(Lang.T("modelo ") + RoleName(r) + Lang.T(".ini importado do jogo")); }
                else if (IsCoopIni(baseIni) && string.Equals(Ini.Get(Ini.Read(baseIni), "net", "role"), RoleName(r), StringComparison.OrdinalIgnoreCase))
                { File.Copy(baseIni, t); AppLog.Write(Lang.T("modelo ") + RoleName(r) + Lang.T(".ini importado do cdmp.ini do jogo")); }
            }
            var asi = Path.Combine(Bin, "cdmp.asi");
            if (File.Exists(asi))
            {
                var sha = Util.Sha256(asi);
                AdoptGameAsi(asi, sha);
            }
        }

        // The mod is the cdmp.asi in the game folder: keep a copy of it and make it the current one.
        public Profile AdoptGameAsi(string asi = null, string sha = null)
        {
            asi = asi ?? Path.Combine(Bin, "cdmp.asi");
            if (!File.Exists(asi)) return cfg.Current;
            sha = sha ?? Util.Sha256(asi);
            var p = ProfileBySha(sha);
            if (p == null)
            {
                p = AddProfile(asi, BuildName(asi, sha) ?? Lang.T("Build local ") + File.GetLastWriteTime(asi).ToString("dd/MM HH:mm"), true);
                p.ExpectedBuild = Util.BuildOf(Util.GameVersion(Bin));
            }
            else if (!File.Exists(CachePath(sha, ".asi"))) CacheStore(asi, ".asi");
            p.AsiPath = CachePath(sha, ".asi");
            cfg.SelectedProfile = p.Name;
            cfg.DeployedAsiSha = sha;
            MirrorToServer(asi, sha, p.Name);
            return p;
        }

        // "r62" when the same file is one of the project's saved builds (cdmp_ns_3663_r62.asi), next to the launcher.
        string BuildName(string asi, string sha)
        {
            try
            {
                var len = new FileInfo(asi).Length;
                foreach (var dir in new[] { cfg.ProjectDir, Path.GetDirectoryName(Path.GetDirectoryName(Config.ExeDir)), Path.GetDirectoryName(Config.ExeDir) })
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir, "cdmp_ns_*_r*.asi"))
                    {
                        if (new FileInfo(f).Length != len || Util.Sha256(f) != sha) continue;
                        var m = Regex.Match(Path.GetFileName(f), @"_r(\d+)\.asi$");
                        if (m.Success) return "r" + m.Groups[1].Value;
                    }
                }
            }
            catch { }
            return null;
        }

        // config\mod is the mod package that travels with the app. Into the game when the game has no mod, and once
        // each time the package changes (a new version dropped into config\mod); a build copied straight into the game
        // is left alone while the package stays the same.
        public List<string> SyncPackage()
        {
            var did = new List<string>();
            if (!Directory.Exists(Bin) || cfg.ModDisabled || !File.Exists(Path.Combine(Bin, "CrimsonDesert.exe"))) return did;
            var pAsi = Path.Combine(TemplateDir, "cdmp.asi");
            var gAsi = Path.Combine(Bin, "cdmp.asi");
            if (File.Exists(pAsi))
            {
                var sha = Util.Sha256(pAsi);
                var gameSha = File.Exists(gAsi) ? Util.Sha256(gAsi) : "";
                if (gameSha != sha && (gameSha == "" || sha != cfg.PackageAsiSha))
                {
                    var ver = PackageVersion();
                    InstallAsi(pAsi, ver ?? BuildName(pAsi, sha) ?? Lang.T("Pacote ") + File.GetLastWriteTime(pAsi).ToString("dd/MM HH:mm"), Util.BuildOf(Util.GameVersion(Bin)));
                    did.Add("cdmp.asi" + (ver != null ? " (" + ver + ")" : ""));
                }
                cfg.PackageAsiSha = sha;
            }
            var pW = Path.Combine(TemplateDir, "winmm.dll");
            var gW = Path.Combine(Bin, "winmm.dll");
            if (File.Exists(pW))
            {
                if (string.IsNullOrEmpty(cfg.WinmmSha)) cfg.WinmmSha = CacheStore(pW, ".dll");
                if (!File.Exists(gW)) { Util.CopyFile(pW, gW); did.Add("winmm.dll"); }
            }
            // ini: a changed package ini replaces the game's (backup first); a missing one is created
            var iniSha = string.Join("|", new[] { "cdmp.host.ini", "cdmp.guest.ini" }.Select(n => Path.Combine(TemplateDir, n)).Select(f => File.Exists(f) ? Util.Sha256(f) : ""));
            bool iniChanged = cfg.PackageIniSha != "" && iniSha != cfg.PackageIniSha;
            foreach (var (tpl, games) in new[] { ("cdmp.host.ini", new[] { "cdmp.host.ini" }), ("cdmp.guest.ini", new[] { "cdmp.ini" }) })
            {
                var t = Path.Combine(TemplateDir, tpl);
                if (!File.Exists(t)) continue;
                foreach (var g in games)
                {
                    var gi = Path.Combine(Bin, g);
                    bool missing = !File.Exists(gi);
                    bool replace = iniChanged && File.Exists(gi) && (g == "cdmp.host.ini" || IsCoopIni(gi) || g == "cdmp.ini");
                    if (!missing && !replace) continue;
                    if (File.Exists(gi)) Backup(gi, "ini");
                    File.Copy(t, gi, true);
                    did.Add(g);
                }
            }
            cfg.PackageIniSha = iniSha;
            foreach (var d in did) AppLog.Write(Lang.T("instalado do pacote: ") + d);
            return did;
        }

        // config\server always holds the mod the game runs: the folder can be zipped as it is, and a deleted
        // config\server comes back from the game.
        void MirrorToServer(string asi, string sha, string name)
        {
            try
            {
                Directory.CreateDirectory(TemplateDir);
                var pAsi = Path.Combine(TemplateDir, "cdmp.asi");
                if (!File.Exists(pAsi) || Util.Sha256(pAsi) != sha) { Util.CopyFile(asi, pAsi); AppLog.Write(Lang.T("config\\server atualizado com o mod do jogo: ") + name); }
                if (PackageVersion() != name) File.WriteAllText(Path.Combine(TemplateDir, "version.txt"), name);
                cfg.PackageAsiSha = sha;
                var pW = Path.Combine(TemplateDir, "winmm.dll");
                var gW = Path.Combine(Bin, "winmm.dll");
                if (!File.Exists(pW) && File.Exists(gW)) Util.CopyFile(gW, pW);
            }
            catch (Exception e) { AppLog.Write("config\\server: " + e.Message); }
        }

        // config\server\version.txt: the package's version name ("r62"), when present.
        public static string PackageVersion()
        {
            try
            {
                var f = Path.Combine(TemplateDir, "version.txt");
                var v = File.Exists(f) ? File.ReadAllText(f).Trim() : "";
                return v.Length > 0 && v.Length <= 40 ? v : null;
            }
            catch { return null; }
        }

        // A new cdmp.asi (an update): the old one stays in the cache, the new one goes into the game.
        public Profile InstallAsi(string newAsi, string label, string build)
        {
            var a = Path.Combine(Bin, "cdmp.asi");
            if (File.Exists(a)) { CacheStore(a, ".asi"); Backup(a, "asi"); }
            Util.CopyFile(newAsi, a);
            var p = AddProfile(a, label, true);
            p.Name = label; p.ExpectedBuild = build;
            return AdoptGameAsi(a);
        }

        public void SaveTemplateFromGame(Role r)
        {
            var src = Path.Combine(Bin, IniName(cfg.Current, r));
            if (!File.Exists(src)) throw new Exception(Path.GetFileName(src) + Lang.T(" não existe no jogo"));
            Directory.CreateDirectory(TemplateDir);
            var dst = TemplatePath(cfg.Current, r);
            if (File.Exists(dst)) Backup(dst, "templates");
            File.Copy(src, dst, true);
        }

        // ---------- status / restore

        public class FileState { public string Name, State; public bool Ok; }

        public List<FileState> Check()
        {
            var r = new List<FileState>();
            var w = Path.Combine(Bin, "winmm.dll");
            r.Add(new FileState
            {
                Name = Lang.T("winmm.dll (carregador)"),
                Ok = File.Exists(w) || cfg.ModDisabled,
                State = cfg.ModDisabled ? Lang.T("mod desligado (volta no próximo HOST/GUEST)") : !File.Exists(w) ? (HasWinmmCopy ? Lang.T("FALTANDO (cópia disponível)") : Lang.T("FALTANDO (sem cópia!)")) : "ok",
            });
            var a = Path.Combine(Bin, "cdmp.asi");
            string st; bool ok = File.Exists(a);
            if (!ok) st = DeployedAsiCopy != null ? Lang.T("FALTANDO (cópia disponível)") : Lang.T("FALTANDO");
            else
            {
                var sha = Util.Sha256(a);
                var p = ProfileBySha(sha);
                st = p != null ? p.Name : Lang.T("desconhecido (") + sha.Substring(0, 8) + ")";
            }
            r.Add(new FileState { Name = Lang.T("cdmp.asi (mod)"), Ok = ok, State = st });
            // the two settings every PC can use
            foreach (var (n, ro, label) in new[] { ("cdmp.host.ini", Role.Host, Lang.T("Config do host")), ("cdmp.ini", Role.Guest, Lang.T("Config do guest")) })
            {
                var path = Path.Combine(Bin, n);
                var has = File.Exists(path);
                var tpl = File.Exists(TemplatePath(cfg.Current, ro));
                var stale = has && ro == Role.Guest && !IsCoopIni(path);
                r.Add(new FileState
                {
                    Name = label + " (" + n + ")",
                    Ok = has || tpl,
                    State = stale ? Lang.T("desatualizada: clique em Restaurar")
                          : has ? "ok" : tpl ? Lang.T("faltando (recriado do modelo ao abrir)") : Lang.T("faltando e sem modelo"),
                });
            }
            return r;
        }

        bool HasWinmmCopy => !string.IsNullOrEmpty(cfg.WinmmSha) && File.Exists(CachePath(cfg.WinmmSha, ".dll"));
        string DeployedAsiCopy
        {
            get
            {
                if (!string.IsNullOrEmpty(cfg.DeployedAsiSha) && File.Exists(CachePath(cfg.DeployedAsiSha, ".asi"))) return CachePath(cfg.DeployedAsiSha, ".asi");
                return cfg.Current != null ? AsiSource(cfg.Current) : null;
            }
        }

        // Puts back what is missing (never replaces a file that is there). Returns what it did.
        public List<string> RestoreMissing()
        {
            var did = new List<string>();
            if (!Directory.Exists(Bin) || cfg.ModDisabled) return did;
            var w = Path.Combine(Bin, "winmm.dll");
            if (!File.Exists(w) && HasWinmmCopy) { Util.CopyFile(CachePath(cfg.WinmmSha, ".dll"), w); did.Add("winmm.dll"); }
            var pw = Path.Combine(TemplateDir, "winmm.dll");
            if (!File.Exists(w) && File.Exists(pw)) { Util.CopyFile(pw, w); did.Add("winmm.dll"); }
            var a = Path.Combine(Bin, "cdmp.asi");
            var copy = DeployedAsiCopy ?? (File.Exists(Path.Combine(TemplateDir, "cdmp.asi")) ? Path.Combine(TemplateDir, "cdmp.asi") : null);
            if (!File.Exists(a) && copy != null) { Util.CopyFile(copy, a); did.Add("cdmp.asi"); }
            foreach (var (name, r) in new[] { ("cdmp.host.ini", Role.Host), ("cdmp.ini", Role.Guest) })
            {
                var ini = Path.Combine(Bin, name);
                var t = TemplatePath(cfg.Current, r);
                if (!File.Exists(ini) && File.Exists(t)) { File.Copy(t, ini); did.Add(Path.GetFileName(ini)); }
                else if (r == Role.Guest && File.Exists(ini) && File.Exists(t) && !IsCoopIni(ini))
                {
                    Backup(ini, "ini");
                    File.Copy(t, ini, true);
                    AppLog.Write(Lang.T("cdmp.ini do mod antigo trocado pelo do co-op (backup em config\\backups)"));
                }
            }
            foreach (var d in did) AppLog.Write(Lang.T("restaurado: ") + d);
            return did;
        }

        // Play without the mod: the loader is renamed (the restorer leaves it alone until the next HOST/GUEST).
        public void DisableMod()
        {
            var w = Path.Combine(Bin, "winmm.dll");
            if (File.Exists(w))
            {
                if (!HasWinmmCopy) cfg.WinmmSha = CacheStore(w, ".dll");
                if (File.Exists(w + ".off")) File.Delete(w + ".off");
                Util.MoveFile(w, w + ".off");
            }
            cfg.ModDisabled = true;
        }

        // ---------- deploy before a launch

        public class Overrides
        {
            public string PlayerName;
            public string PeerHost;  // guest only
            public int PeerPort;
            public bool Record;
        }

        // asiDecision: called when the game's cdmp.asi is not the chosen profile's; true = put the profile's in.
        public void Deploy(Role role, Profile p, Overrides o, Func<string, bool> asiDecision)
        {
            if (!File.Exists(Path.Combine(Bin, "CrimsonDesert.exe"))) throw new Exception(Lang.T("CrimsonDesert.exe não encontrado em ") + Bin);
            // winmm (back from "mod off" first)
            var w = Path.Combine(Bin, "winmm.dll");
            if (!File.Exists(w) && File.Exists(w + ".off")) { Util.MoveFile(w + ".off", w); AppLog.Write(Lang.T("mod religado")); }
            cfg.ModDisabled = false;
            if (!File.Exists(w))
            {
                if (!HasWinmmCopy) throw new Exception(Lang.T("winmm.dll sumiu do jogo e não há cópia guardada. Baixe do host (Mod) ou coloque manualmente."));
                Util.CopyFile(CachePath(cfg.WinmmSha, ".dll"), w);
                AppLog.Write(Lang.T("winmm.dll recolocado"));
            }
            else if (!HasWinmmCopy) cfg.WinmmSha = CacheStore(w, ".dll");

            // asi
            if (p == null) throw new Exception(Lang.T("Nenhum perfil de mod escolhido (aba Mod)."));
            var src = AsiSource(p) ?? throw new Exception(Lang.T("O arquivo do perfil '") + p.Name + Lang.T("' sumiu e não há cópia guardada."));
            var a = Path.Combine(Bin, "cdmp.asi");
            if (!File.Exists(a))
            {
                Util.CopyFile(src, a);
                AppLog.Write(Lang.T("cdmp.asi colocado: ") + p.Name);
            }
            else
            {
                var cur = Util.Sha256(a);
                if (cur != p.AsiSha)
                {
                    var other = ProfileBySha(cur);
                    var desc = other != null ? "'" + other.Name + "'" : Lang.T("um build que não conheco (") + cur.Substring(0, 8) + ")";
                    if (asiDecision == null || asiDecision(desc))
                    {
                        if (other == null) { CacheStore(a, ".asi"); AppLog.Write(Lang.T("cópia do asi que estava no jogo guardada (") + cur.Substring(0, 8) + ")"); }
                        Util.CopyFile(src, a);
                        AppLog.Write(Lang.T("cdmp.asi trocado para: ") + p.Name);
                    }
                    else
                    {
                        // keep the game's and remember it as a profile so the restore puts back the same one
                        var kept = other ?? AddProfile(a, Lang.T("Build do jogo ") + DateTime.Now.ToString("dd-MM HHmm"), true);
                        if (other == null) { kept.AsiPath = CachePath(kept.AsiSha, ".asi"); kept.ExpectedBuild = p.ExpectedBuild; }
                        p = kept;
                        cfg.SelectedProfile = kept.Name;
                        AppLog.Write(Lang.T("cdmp.asi do jogo mantido: ") + kept.Name);
                    }
                }
            }
            if (!File.Exists(CachePath(p.AsiSha, ".asi")) && File.Exists(a) && Util.Sha256(a) == p.AsiSha) CacheStore(a, ".asi");
            cfg.DeployedAsiSha = p.AsiSha;

            // ini: edit in place (keeps hand edits), or create from the template
            var ini = Path.Combine(Bin, IniName(p, role));
            if (File.Exists(ini) && role == Role.Guest && !IsCoopIni(ini) && File.Exists(TemplatePath(p, role)))
            {
                Backup(ini, "ini");
                File.Delete(ini);
                AppLog.Write(Path.GetFileName(ini) + Lang.T(" era de um mod antigo: trocado pelo modelo (backup em config\\backups)"));
            }
            if (!File.Exists(ini))
            {
                var t = TemplatePath(p, role);
                if (!File.Exists(t)) throw new Exception(Lang.T("Sem ") + Path.GetFileName(ini) + Lang.T(" no jogo e sem modelo. ") + (role == Role.Guest ? Lang.T("Use 'Baixar arquivos do host' na aba Mod.") : Lang.T("Coloque o ini e use 'Salvar ini do jogo como modelo'.")));
                File.Copy(t, ini);
                AppLog.Write(Path.GetFileName(ini) + Lang.T(" criado do modelo"));
            }
            var lines = Ini.Read(ini);
            bool ch = false;
            ch |= Ini.Set(lines, "general", "player_name", Clean(o.PlayerName));
            ch |= Ini.Set(lines, "net", "role", RoleName(role));
            ch |= Ini.Set(lines, "net", "transport", "udp");
            ch |= Ini.Set(lines, "net", "peer_port", o.PeerPort.ToString());
            if (role == Role.Host) ch |= Ini.Set(lines, "net", "host_loopback_only", "0");
            if (o.Record) AppLog.Write(Lang.T("ATENÇÃO: gravação de pacotes ligada (record=1): dezenas de MB por minuto. Limpe em Ferramentas."));
            else ch |= Ini.Set(lines, "net", "peer_host", o.PeerHost);
            ch |= Ini.Set(lines, "sniffer", "record", o.Record ? "1" : "0");
            if (ch)
            {
                Backup(ini, "ini");
                Ini.Write(ini, lines);
                AppLog.Write(Path.GetFileName(ini) + Lang.T(": nome=") + Clean(o.PlayerName) + (role == Role.Guest ? Lang.T(" host=") + o.PeerHost : "") + Lang.T(" gravação=") + (o.Record ? "on" : "off"));
            }
        }

        static string Clean(string n)
        {
            n = (n ?? "").Trim();
            n = Regex.Replace(n, @"[\r\n=;\[\]]", "");
            return n.Length == 0 ? "Player" : n.Length > 24 ? n.Substring(0, 24) : n;
        }

        public static void Backup(string file, string kind)
        {
            try
            {
                var d = Util.IsModBinary(file) ? Path.Combine(TemplateDir, "backups") : Path.Combine(BackupDir, kind);
                Directory.CreateDirectory(d);
                Util.CopyFile(file, Path.Combine(d, Path.GetFileName(file) + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
                foreach (var old in new DirectoryInfo(d).GetFiles(Path.GetFileName(file) + ".*").OrderByDescending(f => f.LastWriteTime).Skip(20))
                    old.Delete();
            }
            catch (Exception e) { AppLog.Write(Lang.T("backup falhou: ") + e.Message); }
        }

        // ---------- saves

        public static string CdDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pearl Abyss", "CD");
        public static string GuestSaveDir(uint id) => Path.Combine(CdDir, "save_cdmp_guest", id.ToString());
        public static string HostSaveDir(uint id) => Path.Combine(CdDir, "save", id.ToString());

        // The folder that holds slotN folders inside what the user picked (handles <id>\<id>\slotN nesting).
        static string FindSlotRoot(string dir)
        {
            if (Directory.GetDirectories(dir, "slot*").Length > 0) return dir;
            foreach (var d in Directory.GetDirectories(dir))
            {
                var r = FindSlotRoot(d);
                if (r != null) return r;
            }
            return null;
        }

        public static string BackupFolderZip(string dir, string tag)
        {
            if (!Directory.Exists(dir) || !Directory.EnumerateFileSystemEntries(dir).Any()) return null;
            var d = Path.Combine(BackupDir, "saves");
            Directory.CreateDirectory(d);
            var z = Path.Combine(d, tag + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
            ZipFile.CreateFromDirectory(dir, z, CompressionLevel.Optimal, false);
            return z;
        }

        // Zip file or folder -> save_cdmp_guest\<id>\ (backup of what was there first).
        public static string ImportGuestSave(string source, uint id)
        {
            if (id == 0) throw new Exception(Lang.T("Conta Steam deste PC não encontrada (abra o Steam uma vez)."));
            return ImportGuestSaveTo(source, GuestSaveDir(id));
        }

        public static string ImportGuestSaveTo(string source, string dst)
        {
            string tmp = null;
            try
            {
                string root;
                if (File.Exists(source) && source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    tmp = Path.Combine(Path.GetTempPath(), "cdmp_save_" + Guid.NewGuid().ToString("N"));
                    ZipFile.ExtractToDirectory(source, tmp);
                    root = FindSlotRoot(tmp);
                }
                else root = Directory.Exists(source) ? FindSlotRoot(source) : null;
                if (root == null) throw new Exception(Lang.T("Nenhuma pasta slotN encontrada nesse save."));
                var bk = BackupFolderZip(dst, "guest_" + Path.GetFileName(dst));
                if (bk != null) AppLog.Write(Lang.T("backup do save do guest: ") + bk);
                Directory.CreateDirectory(dst);
                foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    var rel = f.Substring(root.Length).TrimStart('\\');
                    if (rel.EndsWith("steam_autocloud.vdf", StringComparison.OrdinalIgnoreCase)) continue;
                    Util.CopyFile(f, Path.Combine(dst, rel));
                }
                return dst;
            }
            finally { if (tmp != null) try { Directory.Delete(tmp, true); } catch { } }
        }

        public static string ZipSaveForShare(string dir)
        {
            var root = Directory.Exists(dir) ? FindSlotRoot(dir) : null;
            if (root == null) throw new Exception(Lang.T("pasta de save sem slots: ") + dir);
            var z = Path.Combine(Path.GetTempPath(), "cdmp_share_save.zip");
            if (File.Exists(z)) File.Delete(z);
            using (var zip = ZipFile.Open(z, ZipArchiveMode.Create))
                foreach (var f in Directory.GetFiles(root, "*.save", SearchOption.AllDirectories))
                    zip.CreateEntryFromFile(f, f.Substring(root.Length).TrimStart('\\'), CompressionLevel.Optimal);
            return z;
        }

        public static string BackupAllSaves()
        {
            var d = Path.Combine(BackupDir, "saves");
            Directory.CreateDirectory(d);
            var z = Path.Combine(d, "todos_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
            using (var zip = ZipFile.Open(z, ZipArchiveMode.Create))
                foreach (var sub in new[] { "save", "save_cdmp_guest", "cdmp_save_sandbox" })
                {
                    var s = Path.Combine(CdDir, sub);
                    if (!Directory.Exists(s)) continue;
                    foreach (var f in Directory.GetFiles(s, "*", SearchOption.AllDirectories))
                        zip.CreateEntryFromFile(f, sub + "\\" + f.Substring(s.Length).TrimStart('\\'), CompressionLevel.Optimal);
                }
            return z;
        }

        // ---------- logs, dumps, cleanup

        static readonly Regex DatedLog = new Regex(@"^cdmp(\.(host|guest))?_\d{8}-\d{6}(_\d+)?\.log$", RegexOptions.IgnoreCase);

        public List<FileInfo> Recordings() =>
            Directory.Exists(Bin) ? new DirectoryInfo(Bin).GetFiles("cdmp_packets*").ToList() : new List<FileInfo>();

        // Dated logs beyond the newest `keep` of each role (never the newest, which the alias points to).
        public List<FileInfo> OldLogs(int keep)
        {
            var r = new List<FileInfo>();
            if (!Directory.Exists(Bin)) return r;
            var all = new DirectoryInfo(Bin).GetFiles("cdmp*.log").Where(f => DatedLog.IsMatch(f.Name) || Regex.IsMatch(f.Name, @"^cdmp\.\d+\.log$"));
            foreach (var g in all.GroupBy(f => f.Name.Contains(".host") ? "h" : f.Name.Contains(".guest") ? "g" : "n"))
                r.AddRange(g.OrderByDescending(f => f.LastWriteTime).Skip(Math.Max(1, keep)));
            r.AddRange(new DirectoryInfo(Bin).GetFiles("cdmp*.extra_*.log"));
            return r;
        }

        public List<FileInfo> OldDumps(int days) =>
            Directory.Exists(Bin) ? new DirectoryInfo(Bin).GetFiles("cdmp_crash*.dmp").Where(f => f.LastWriteTime < DateTime.Now.AddDays(-days)).ToList() : new List<FileInfo>();

        public static long Total(IEnumerable<FileInfo> fs) => fs.Sum(f => { try { return f.Length; } catch { return 0L; } });

        public static int DeleteAll(IEnumerable<FileInfo> fs, out long freed)
        {
            int n = 0; freed = 0;
            foreach (var f in fs)
            {
                try { var l = f.Length; f.Delete(); n++; freed += l; }
                catch { }  // in use by a running game: left
            }
            return n;
        }

        // Newest logs, ini, crash dumps of the last 2 days and the game's dump reports -> one zip on the Desktop.
        public string CollectLogs()
        {
            var z = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "cdmp-logs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
            using (var zip = ZipFile.Open(z, ZipArchiveMode.Create))
            {
                void Add(string path, string entry, long cap = 200L << 20)
                {
                    try
                    {
                        var fi = new FileInfo(path);
                        if (!fi.Exists) return;
                        var e = zip.CreateEntry(entry, CompressionLevel.Optimal);
                        using (var o = e.Open())
                        using (var i = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            if (i.Length > cap)
                            {   // giant logs: first 5 MB + last part
                                var head = new byte[5 << 20]; i.Read(head, 0, head.Length); o.Write(head, 0, head.Length);
                                var mark = System.Text.Encoding.ASCII.GetBytes("\r\n\r\n===== [launcher: trecho do meio cortado] =====\r\n\r\n");
                                o.Write(mark, 0, mark.Length);
                                i.Seek(-(cap - head.Length), SeekOrigin.End);
                            }
                            i.CopyTo(o);
                        }
                    }
                    catch { }
                }
                if (Directory.Exists(Bin))
                {
                    var di = new DirectoryInfo(Bin);
                    foreach (var g in di.GetFiles("cdmp*.log").Where(f => DatedLog.IsMatch(f.Name)).GroupBy(f => f.Name.Contains(".host") ? "h" : f.Name.Contains(".guest") ? "g" : "n"))
                        foreach (var f in g.OrderByDescending(f => f.LastWriteTime).Take(3)) Add(f.FullName, "logs/" + f.Name);
                    foreach (var f in di.GetFiles("cdmp*.ini")) Add(f.FullName, "ini/" + f.Name);
                    foreach (var f in di.GetFiles("cdmp_crash*.dmp").Where(f => f.LastWriteTime > DateTime.Now.AddDays(-2))) Add(f.FullName, "dumps/" + f.Name);
                }
                var rep = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pearl Abyss", "DumpCache", "reports");
                if (Directory.Exists(rep))
                    foreach (var f in new DirectoryInfo(rep).GetFiles("*", SearchOption.AllDirectories).Where(f => f.LastWriteTime > DateTime.Now.AddDays(-2)))
                        Add(f.FullName, "game-reports/" + f.FullName.Substring(rep.Length).TrimStart('\\').Replace('\\', '/'));
                Add(Path.Combine(Config.DataDir, "launcher.log"), "launcher.log");
                var info = zip.CreateEntry("info.txt");
                using (var w = new StreamWriter(info.Open()))
                {
                    w.WriteLine("jogo: " + Util.GameVersion(Bin) + "  pasta: " + Bin);
                    w.WriteLine("perfil: " + cfg.Current?.Name + "  asi no jogo: " + (File.Exists(Path.Combine(Bin, "cdmp.asi")) ? Util.Sha256(Path.Combine(Bin, "cdmp.asi")) : "faltando"));
                    w.WriteLine("data: " + DateTime.Now);
                }
            }
            return z;
        }
    }
}
