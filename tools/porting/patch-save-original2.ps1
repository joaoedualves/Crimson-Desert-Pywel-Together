$ErrorActionPreference = 'Stop'
$lat = [Text.Encoding]::GetEncoding(28591)
$root = '<root>'
$ns = "$root\nativeserver\src"
function Rpl($p, $old, $new) {
  $t = [IO.File]::ReadAllText($p, $lat)
  $n = ([regex]::Matches($t, [regex]::Escape($old))).Count
  if ($n -ne 1) { throw "$p : ancora achada $n vezes: $old" }
  [IO.File]::WriteAllText($p, $t.Replace($old, $new), $lat)
}
# Redirector: o filtro so vale depois de ligado (atomico)
Rpl "$ns\common\save_guard.h" '    void set_only(std::wstring component) { only_ = std::move(component); }' @'
    // set_only() before the hooks exist; enable_only() switches it on later (any thread).
    void set_only(std::wstring component) { only_ = std::move(component); }
    void enable_only() { only_on_.store(true, std::memory_order_release); }
    bool only_enabled() const { return only_on_.load(std::memory_order_acquire); }
'@.TrimEnd("`r","`n")
Rpl "$ns\common\save_guard.h" '    std::wstring only_;     // see set_only()' @'
    std::wstring only_;     // see set_only()
    std::atomic<bool> only_on_{false};
'@.TrimEnd("`r","`n")
Rpl "$ns\common\save_guard.cpp" '    if (!only_.empty()) {' '    if (!only_.empty() && only_on_.load(std::memory_order_acquire)) {'
# save_hooks: funcao para ligar
Rpl "$ns\mod\session2\save_hooks.cpp" @'
        log::warn("Session2 save guard: the host's own saves go to the REAL save folder; only the guest's slot204 "
                  "is kept in the sandbox");
'@.TrimEnd("`r","`n") ''
Rpl "$ns\mod\session2\save_hooks.cpp" 'bool save_hooks_guest_role() { return g_guest_role.load(); }' @'
bool save_hooks_guest_role() { return g_guest_role.load(); }

// Called once the guest's own slot is working (guest_own_save=1 installed): from now on the host's own saves are
// read and written in the REAL save folder; only the guest's slot204 stays in the sandbox.
void save_hooks_host_real_saves() {
    if (!g_redir || g_redir->only_enabled()) return;
    g_redir->enable_only();
    log::warn("Session2 save guard: the host plays on its ORIGINAL save folder (read and written there); only the "
              "guest's slot204 is kept in the sandbox");
}
'@.TrimEnd("`r","`n")
Rpl "$ns\mod\session2\save_hooks.h" 'bool save_hooks_guest_role();' @'
bool save_hooks_guest_role();
void save_hooks_host_real_saves();
'@.TrimEnd("`r","`n")
# guest_save_host: liga quando o save proprio do guest instalou em modo On
$p = "$ns\mod\session2\guest_save_host.cpp"; $t = [IO.File]::ReadAllText($p, $lat)
$rx = [regex]'    return true;(\r?\n)\}(\r?\n)(\r?\n)gsv::Mode guest_save_mode\(\)'
if ($rx.Matches($t).Count -ne 1) { throw 'ancora guest_save_install' }
$t = $rx.Replace($t, '    if (opt.mode == Mode::On) save_hooks_host_real_saves();$1    return true;$1}$2$3gsv::Mode guest_save_mode()')
[IO.File]::WriteAllText($p, $t, $lat)
"ok"
& 'bg.ps1' "powershell -NoProfile -ExecutionPolicy Bypass -File '$root\porte-3732\build-r67.ps1'"
