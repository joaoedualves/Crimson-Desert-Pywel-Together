using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CdmpLauncher
{
    // One window per Windows user. A second start (e.g. "CD-Pywel-Together.exe --host") passes its command to the
    // window that is already open through a local named pipe and exits:
    //   --host   = Abrir como host      --guest = Entrar no mundo      (no argument) = bring the window to the front
    public static class Instance
    {
        static string PipeName => "PywelTogether-" + Environment.UserName;
        static Mutex mutex;

        public static bool IsFirst()
        {
            mutex = new Mutex(true, @"Local\PywelTogether-" + Environment.UserName, out var first);
            return first;
        }

        // Second start: hands the command over. True = the open window took it.
        public static bool Send(string command)
        {
            try
            {
                using (var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    c.Connect(3000);
                    var b = Encoding.UTF8.GetBytes(command + "\n");
                    c.Write(b, 0, b.Length);
                    c.Flush();
                    return true;
                }
            }
            catch { return false; }
        }

        // First start: listens for commands for the life of the app (this user only, never from the network).
        public static void Listen(Action<string> onCommand)
        {
            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        var sec = new PipeSecurity();
                        sec.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                        sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
                        using (var s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None, 256, 256, sec))
                        {
                            s.WaitForConnection();
                            using (var r = new StreamReader(s, Encoding.UTF8))
                            {
                                var cmd = (r.ReadLine() ?? "").Trim();
                                if (cmd.Length > 0 && cmd.Length < 64) onCommand(cmd);
                            }
                        }
                    }
                    catch { Thread.Sleep(500); }
                }
            });
        }
    }
}
