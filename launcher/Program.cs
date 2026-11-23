using System;
using System.Windows.Forms;

namespace CdmpLauncher
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--friends") return SteamFriends.RunChild(args[1]);
            if (args.Length >= 4 && args[0] == "--selftest") return SelfTest.Run(args[1], args[2], args[3]);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => { AppLog.Write(Lang.T("ERRO: ") + e.Exception); MessageBox.Show(e.Exception.Message, "Crimson Desert: Pywel Together"); };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => AppLog.Write(Lang.T("ERRO FATAL: ") + e.ExceptionObject);
            if (args.Length >= 2 && args[0] == "--render")
            {
                Lang.English = args.Length >= 3 && args[2] == "en";
                if (args.Length >= 4 && args[3] == "picker") { LanguagePicker.RenderTo(args[1]); return 0; }
                MainForm.RenderTabs(args[1]); return 0;
            }
            // --host / --guest (or nothing) while the app is already open: the open window does it
            var command = Array.Find(args, x => x == "--host" || x == "--guest");
            if (!Instance.IsFirst())
            {
                Instance.Send(command ?? "--show");   // the open window may still be at the language choice: no second window either way
                return 0;
            }
            var cfg = Config.Load();
            if (cfg.Language != "pt" && cfg.Language != "en")
            {
                var pick = LanguagePicker.Ask();
                if (pick == null) return 0;
                cfg.Language = pick;
                try { cfg.Save(); } catch { }
            }
            Lang.English = cfg.Language == "en";
            var form = new MainForm { StartCommand = command };
            Instance.Listen(cmd => { try { form.BeginInvoke((Action)(() => form.OnCommand(cmd))); } catch { } });
            Application.Run(form);
            return 0;
        }
    }
}
