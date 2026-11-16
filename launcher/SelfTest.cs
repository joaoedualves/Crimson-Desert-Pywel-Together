using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace CdmpLauncher
{
    // CDMP-Launcher.exe --selftest <out.txt> <realGameDir> <projectDir>
    // Checks the logic without the window and without touching the real game folder: everything is written to a
    // fake bin64 and a fake save folder under %TEMP%.
    static class SelfTest
    {
        static StringBuilder o = new StringBuilder();
        static int fails;
        static void Ok(bool c, string what) { o.AppendLine((c ? "OK   " : "FALHA ") + what); if (!c) fails++; }

        public static int Run(string outFile, string realBin, string proj)
        {
            try { RunAsync(realBin, proj).GetAwaiter().GetResult(); }
            catch (Exception e) { o.AppendLine("EXCECAO " + e); fails++; }
            o.AppendLine(fails == 0 ? "TUDO OK" : fails + " FALHA(S)");
            File.WriteAllText(outFile, o.ToString());
            return fails;
        }

        static async Task RunAsync(string realBin, string proj)
        {
            var root = Path.Combine(Path.GetTempPath(), "cdmp_selftest");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            var bin = Path.Combine(root, "bin64");
            Directory.CreateDirectory(bin);
            Environment.SetEnvironmentVariable("CDMP_LAUNCHER_DATA", Path.Combine(root, "data"));

            // detection
            var found = Util.FindGameDir();
            o.AppendLine("jogo detectado: " + found + "  versão " + Util.GameVersion(found) + "  steamid32 " + Util.SteamId32() + "  ip " + Util.MainLanIp());
            Ok(found.Length > 0, "acha o jogo pelo Steam");
            Ok(Util.BuildOf(Util.GameVersion(realBin)) == "3663", "le a versão do exe (3663)");

            // fake bin64 with the real files
            File.WriteAllText(Path.Combine(bin, "CrimsonDesert.exe"), "fake");
            File.Copy(Path.Combine(realBin, "winmm.dll"), Path.Combine(bin, "winmm.dll"));
            File.Copy(Path.Combine(realBin, "cdmp.asi"), Path.Combine(bin, "cdmp.asi"));
            File.Copy(Path.Combine(realBin, "cdmp.host.ini"), Path.Combine(bin, "cdmp.host.ini"));
            File.Copy(Path.Combine(realBin, "cdmp.guest.ini"), Path.Combine(bin, "cdmp.guest.ini"));
            // an old mod's cdmp.ini (transport=steam, no [session2]), as in the PC's game folder
            File.WriteAllLines(Path.Combine(bin, "cdmp.ini"), new[] { "[general]", "player_name=Velho", "[net]", "enabled=1", "transport=steam", "role=guest" });

            var cfg = new Config { GameDir = bin, ProjectDir = proj };
            var dep = new Deployer(cfg);
            dep.CaptureFromGame();
            Ok(File.Exists(Path.Combine(Deployer.TemplateDir, "cdmp.host.ini")) && File.Exists(Path.Combine(Deployer.TemplateDir, "cdmp.guest.ini")), "captura modelos de ini");
            Ok(cfg.WinmmSha != "" && File.Exists(Deployer.CachePath(cfg.WinmmSha, ".dll")), "guarda cópia do winmm");
            var n = dep.ImportProjectBuilds();
            o.AppendLine("perfis: " + string.Join(" | ", cfg.Profiles.Select(p => p.Name + (p.Legacy ? " [antigo]" : "") + " b" + p.ExpectedBuild)));
            Ok(cfg.Profiles.Count >= 2, "importa builds do projeto");
            var gameSha = Util.Sha256(Path.Combine(bin, "cdmp.asi"));
            Ok(dep.ProfileBySha(gameSha) != null, "reconhece o asi que está no jogo");

            // restorer
            File.Delete(Path.Combine(bin, "winmm.dll"));
            File.Delete(Path.Combine(bin, "cdmp.asi"));
            File.Delete(Path.Combine(bin, "cdmp.host.ini"));
            var did = dep.RestoreMissing();
            Ok(did.Contains("winmm.dll") && did.Contains("cdmp.asi") && did.Contains("cdmp.host.ini"), "restaura winmm/asi/ini apagados: " + string.Join(",", did));
            Ok(Deployer.IsCoopIni(Path.Combine(bin, "cdmp.ini")) && Deployer.IsCoopIni(Path.Combine(bin, "cdmp.guest.ini")), "Restaurar troca o cdmp.ini do mod antigo pelo co-op");
            Ok(dep.EnvRole(Role.Host) == "host" && dep.EnvRole(Role.Guest) == null, "papel: host=host, guest em outro PC sem CDMP_ROLE");
            Ok(dep.IniName(null, Role.Guest) == "cdmp.ini" && dep.LogAlias(null, Role.Guest).EndsWith("cdmp.log"), "guest em outro PC usa cdmp.ini e cdmp.log");
            Ok(Util.Sha256(Path.Combine(bin, "cdmp.asi")) == gameSha, "asi restaurado e o mesmo (hash)");

            // deploy with a different profile: the question is asked, and 'no' keeps the game's
            var other = cfg.Profiles.First(p => p.AsiSha != gameSha && !p.Legacy);
            cfg.SelectedProfile = other.Name;
            string asked = null;
            dep.Deploy(Role.Host, cfg.Current, new Deployer.Overrides { PlayerName = "Player Host", PeerPort = 37015, Record = false }, d => { asked = d; return false; });
            Ok(asked != null, "pergunta quando o asi do jogo e de outro perfil (" + asked + ")");
            Ok(Util.Sha256(Path.Combine(bin, "cdmp.asi")) == gameSha, "resposta Não mantem o asi do jogo");
            var hl = Ini.Read(Path.Combine(bin, "cdmp.host.ini"));
            Ok(Ini.Get(hl, "general", "player_name") == "Player Host", "troca o nome do host no ini");
            Ok(Ini.Get(hl, "net", "host_loopback_only") == "0" && Ini.Get(hl, "sniffer", "record") == "0", "host escuta na LAN, gravação off");
            var origLines = File.ReadAllLines(Path.Combine(realBin, "cdmp.host.ini")).Length;
            Ok(Math.Abs(hl.Count - origLines) <= 2, "ini editado no lugar mantem o resto (" + origLines + " -> " + hl.Count + " linhas)");
            Ok(Ini.Get(hl, "session2", "share_field") == "1", "chaves do session2 intactas");

            cfg.SelectedProfile = other.Name;
            dep.Deploy(Role.Guest, cfg.Current, new Deployer.Overrides { PlayerName = "Player Guest", PeerHost = "192.168.0.10", PeerPort = 37015, Record = false }, d => true);
            var gl = Ini.Read(Path.Combine(bin, "cdmp.ini"));
            Ok(Deployer.IsCoopIni(Path.Combine(bin, "cdmp.ini")) && Ini.Get(gl, "net", "transport") == "udp", "cdmp.ini velho trocado pelo modelo co-op (udp)");
            Ok(Ini.Get(gl, "net", "peer_host") == "192.168.0.10" && Ini.Get(gl, "general", "player_name") == "Player Guest" && Ini.Get(gl, "net", "role") == "guest", "ini do guest com IP do host e nome");
            Ok(Ini.Get(gl, "sniffer", "record") == "0", "gravação do guest desligada (o modelo tinha record=1)");
            Ok(Util.Sha256(Path.Combine(bin, "cdmp.asi")) == other.AsiSha, "resposta Sim troca o asi para o perfil");
            Ok(File.Exists(Deployer.CachePath(gameSha, ".asi")), "guardou cópia do asi antigo antes de trocar");
            Ok(Directory.GetFiles(Path.Combine(Deployer.BackupDir, "ini")).Length >= 2, "backup do ini antes de editar");

            // the package in data\mod: first install, a build copied straight into the game is kept, a new package goes in
            var builds = Directory.GetFiles(proj, "cdmp_ns_*_r*.asi").OrderBy(File.GetLastWriteTime).ToList();
            if (builds.Count >= 3)
            {
                var pkgAsi = Path.Combine(Deployer.TemplateDir, "cdmp.asi");
                File.Copy(builds[0], pkgAsi, true);
                File.WriteAllText(Path.Combine(Deployer.TemplateDir, "version.txt"), "rPKG");
                File.Delete(Path.Combine(bin, "cdmp.asi"));
                dep.SyncPackage();
                Ok(Util.Sha256(Path.Combine(bin, "cdmp.asi")) == Util.Sha256(builds[0]) && cfg.Current.Name == "rPKG", "pacote: instala o mod quando o jogo não tem");
                File.Copy(builds[1], Path.Combine(bin, "cdmp.asi"), true);
                dep.SyncPackage();
                Ok(Util.Sha256(Path.Combine(bin, "cdmp.asi")) == Util.Sha256(builds[1]), "pacote: build copiado direto no jogo fica");
                File.Copy(builds[2], pkgAsi, true);
                dep.SyncPackage();
                Ok(Util.Sha256(Path.Combine(bin, "cdmp.asi")) == Util.Sha256(builds[2]), "pacote: versão nova no pacote vai para o jogo");
                File.Delete(pkgAsi); File.Delete(Path.Combine(Deployer.TemplateDir, "version.txt"));
                File.Copy(Path.Combine(realBin, "cdmp.asi"), Path.Combine(bin, "cdmp.asi"), true);
                dep.AdoptGameAsi();
            }

            // save import: zip with <id>\slotN inside, into a fake save root
            var src = Path.Combine(root, "savesrc", "123456789");
            Directory.CreateDirectory(Path.Combine(src, "slot1"));
            File.WriteAllText(Path.Combine(src, "slot1", "save.save"), "x");
            File.WriteAllText(Path.Combine(src, "slot1", "lobby.save"), "y");
            File.WriteAllText(Path.Combine(src, "steam_autocloud.vdf"), "z");
            var zip = Path.Combine(root, "s.zip");
            ZipFile.CreateFromDirectory(Path.Combine(root, "savesrc"), zip, CompressionLevel.Fastest, true);
            var fakeLocal = Path.Combine(root, "local");
            var dst = ImportInto(zip, fakeLocal, 4242);
            Ok(File.Exists(Path.Combine(dst, "slot1", "save.save")) && !Directory.Exists(Path.Combine(dst, "123456789")), "save importado sem aninhar a pasta do id: " + dst);
            Ok(!File.Exists(Path.Combine(dst, "steam_autocloud.vdf")), "não cópia o steam_autocloud.vdf");

            // network: beacon + finder + download on this PC
            var info = new HostInfo { Name = "TesteHost", State = "titulo", AsiSha = gameSha, AsiName = "r-teste", Game = "1.0.0.3663", Port = 37015 };
            var servedFile = Deployer.CachePath(gameSha, ".asi");
            using (var b = new HostBeacon("LOBO-1234", 47020, 47021, () => info, w => w == "asi" ? servedFile : null))
            {
                b.Start();
                var l = await HostFinder.Find("lobo-1234", 47020, "");
                Ok(l.Count == 1 && l[0].Name == "TesteHost" && l[0].State == "titulo", "guest acha o host pelo código (maiúscula/minúscula tanto faz): " + l.Count);
                var wrong = await HostFinder.Find("URSO-9999", 47020, "", 800);
                Ok(wrong.Count == 0, "código errado não acha nada");
                info.State = "mundo"; info.GuestState = "entrando";
                l = await HostFinder.Find("LOBO-1234", 47020, "127.0.0.1", 800);
                Ok(l.Count == 1 && l[0].InWorld && l[0].GuestState == "entrando", "estado do host atualiza (mundo)");
                var dl = Path.Combine(root, "dl.asi");
                await HostFinder.Download(l[0].Address, 47021, "LOBO-1234", "asi", dl);
                Ok(Util.Sha256(dl) == gameSha, "baixa o asi do host com hash certo");
                try { await HostFinder.Download(l[0].Address, 47021, "URSO-9999", "asi", dl + "2"); Ok(false, "download com código errado recusado"); }
                catch (Exception e) { Ok(e.Message.Contains("recusou"), "download com código errado recusado"); }
            }

            // log parsing on a copy of a real log
            var real = Directory.GetFiles(realBin, "cdmp.guest_*.log").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
            if (real != null)
            {
                var txt = Util.ReadShared(real, 30 << 20);
                o.AppendLine("log de guest real: " + Path.GetFileName(real) + "  game is up=" + txt.Contains("Late init: game is up") + "  REMOTE+ready=" + txt.Split('\n').Any(x => x.Contains("connection REMOTE") && x.Contains("host ready yes")) + "  Local: pos=" + txt.Contains("Local: pos ("));
            }

            // cleanup listing on the real folder (read only)
            var rd = new Deployer(new Config { GameDir = realBin });
            o.AppendLine("gravações no jogo real: " + rd.Recordings().Count + " = " + Util.Size(Deployer.Total(rd.Recordings())) + " | logs antigos (6): " + rd.OldLogs(6).Count + " = " + Util.Size(Deployer.Total(rd.OldLogs(6))));
            var newestHost = new DirectoryInfo(realBin).GetFiles("cdmp.host_*.log").OrderByDescending(f => f.LastWriteTime).First();
            Ok(!rd.OldLogs(1).Any(f => f.Name == newestHost.Name), "limpeza nunca pega o log mais novo");
        }

        // ImportGuestSave with a fake %LOCALAPPDATA%: same code path, other root.
        static string ImportInto(string zip, string fakeLocal, uint id)
        {
            var dst = Path.Combine(fakeLocal, "save_cdmp_guest", id.ToString());
            // Deployer.GuestSaveDir uses SpecialFolder.LocalApplicationData (not redirectable): test the copy logic
            // through the public helper on a fake id folder by calling the same routine with the target swapped.
            return Deployer.ImportGuestSaveTo(zip, dst);
        }
    }
}
