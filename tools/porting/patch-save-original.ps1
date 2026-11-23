$ErrorActionPreference = 'Stop'
$lat = [Text.Encoding]::GetEncoding(28591)
$root = '<root>'
$ns = "$root\nativeserver\src"
$bk = "$root\Pywel-Together\Backup_Code\$(Get-Date -Format 'dd-MM_HH-mm')_Host-salva-no-save-original_backup"
New-Item -ItemType Directory -Force $bk | Out-Null
Copy-Item "$ns\common\save_guard.h","$ns\common\save_guard.cpp","$ns\mod\session2\save_hooks.cpp" $bk
function Rpl($p, $old, $new) {
  $t = [IO.File]::ReadAllText($p, $lat)
  $n = ([regex]::Matches($t, [regex]::Escape($old))).Count
  if ($n -ne 1) { throw "$p : ancora achada $n vezes: $old" }
  [IO.File]::WriteAllText($p, $t.Replace($old, $new), $lat)
}
Rpl "$ns\common\save_guard.h" '    const std::wstring& sandbox() const { return sandbox_; }' @'
    const std::wstring& sandbox() const { return sandbox_; }
    // Only paths with this folder name as a component (lower case, e.g. L"slot204": the guest's slot, also
    // "slot204.xyz") are mapped; every other path stays in the REAL save folder. Empty = everything is mapped.
    void set_only(std::wstring component) { only_ = std::move(component); }
'@.TrimEnd("`r","`n")
Rpl "$ns\common\save_guard.h" '    std::wstring sandbox_;  // as given:' @'
    std::wstring only_;     // see set_only()
    std::wstring sandbox_;  // as given:
'@.TrimEnd("`r","`n")
Rpl "$ns\common\save_guard.cpp" @'
    if (i < p.size() && p[i] != L'\\' && p[i] != L'/') return kNoMatch;
    return i;
'@.TrimEnd("`r","`n") @'
    if (i < p.size() && p[i] != L'\\' && p[i] != L'/') return kNoMatch;
    if (!only_.empty()) {
        std::wstring rest;
        rest.reserve(p.size() - i);
        for (size_t k = i; k < p.size(); ++k) rest.push_back(p[k] == L'/' ? L'\\' : fold(p[k]));
        const std::wstring needle = L"\\" + only_;
        bool hit = false;
        for (size_t pos = rest.find(needle); pos != std::wstring::npos; pos = rest.find(needle, pos + 1)) {
            const size_t e = pos + needle.size();
            if (e >= rest.size() || rest[e] == L'\\' || rest[e] == L'.') {
                hit = true;
                break;
            }
        }
        if (!hit) return kNoMatch;
    }
    return i;
'@.TrimEnd("`r","`n")
Rpl "$ns\mod\session2\save_hooks.cpp" @'
        auto redir = std::make_unique<saveguard::Redirector>(dir, sandbox);
        if (!redir->valid() || !prepare_sandbox(dir, sandbox, copied)) {
'@.TrimEnd("`r","`n") @'
        auto redir = std::make_unique<saveguard::Redirector>(dir, sandbox);
        // The host always plays and saves on its ORIGINAL save folder: only the guest's slot lives in the sandbox.
        redir->set_only(L"slot204");
        log::warn("Session2 save guard: the host's own saves go to the REAL save folder; only the guest's slot204 "
                  "is kept in the sandbox");
        if (!redir->valid() || !prepare_sandbox(dir, sandbox, copied)) {
'@.TrimEnd("`r","`n")
Select-String -Path "$ns\common\save_guard.cpp" -Pattern 'fold\(wchar_t|wchar_t fold' | ForEach-Object { "$($_.LineNumber): $($_.Line.Trim())" }
"ok -> $bk"
& 'bg.ps1' "powershell -NoProfile -ExecutionPolicy Bypass -File '$root\porte-3732\build-r67.ps1'"
