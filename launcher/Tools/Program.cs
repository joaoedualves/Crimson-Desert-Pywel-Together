using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

// pywel-tools.exe: the few actions of Pywel Together that touch other programs, kept out of the main exe.
//   press <pid> <scancode>             one key press in that Crimson Desert window only (the game reads raw input)
//   copy  <src> <dst>                  copies a mod file (cdmp.asi / winmm.dll) atomically
//   move  <src> <dst>                  renames or moves a mod file (overwrites)
// Prints "ok" or "error: <reason>" and exits 0 / 1.
static class Program
{
    [StructLayout(LayoutKind.Sequential)] struct KI { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct MI { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct U { [FieldOffset(0)] public KI k; [FieldOffset(0)] public MI m; }
    [StructLayout(LayoutKind.Sequential)] struct IN { public uint type; public U u; }
    [DllImport("user32.dll")] static extern uint SendInput(uint n, IN[] i, int size);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    static int Main(string[] a)
    {
        try
        {
            string err;
            switch (a.Length > 0 ? a[0] : "")
            {
                case "press": err = Press(int.Parse(a[1]), ushort.Parse(a[2])); break;
                case "copy": err = Copy(a[1], a[2]); break;
                case "move": err = Move(a[1], a[2]); break;
                default: err = "unknown command"; break;
            }
            Console.Out.Write(err == null ? "ok" : "error: " + err);
            return err == null ? 0 : 1;
        }
        catch (Exception e) { Console.Out.Write("error: " + e.Message); return 1; }
    }

    static IntPtr GameWindow(int pid)
    {
        var p = Process.GetProcessById(pid);
        p.Refresh();
        return p.ProcessName == "CrimsonDesert" ? p.MainWindowHandle : IntPtr.Zero;
    }

    static string Press(int pid, ushort scan)
    {
        IntPtr h;
        try { h = GameWindow(pid); } catch { return "process gone"; }
        if (h == IntPtr.Zero) return "no game window";
        for (int i = 0; i < 5 && GetForegroundWindow() != h; i++)
        {
            keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);  // lets this process take the focus
            ShowWindow(h, 9);
            SetForegroundWindow(h);
            Thread.Sleep(300);
        }
        if (GetForegroundWindow() != h) return "window did not come to the front";
        Thread.Sleep(200);
        var size = Marshal.SizeOf(typeof(IN));
        var d = new IN { type = 1 };
        d.u.k.scan = scan; d.u.k.flags = 0x0008;
        SendInput(1, new[] { d }, size);
        Thread.Sleep(90);
        d.u.k.flags = 0x0008 | 0x0002;
        SendInput(1, new[] { d }, size);
        return null;
    }

    static string Copy(string src, string dst)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dst)));
        var tmp = dst + ".part";
        using (var i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var o = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            i.CopyTo(o);
        if (File.Exists(dst)) File.Delete(dst);
        File.Move(tmp, dst);
        return null;
    }

    static string Move(string src, string dst)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dst)));
        if (File.Exists(dst)) File.Delete(dst);
        File.Move(src, dst);
        return null;
    }
}
