$ErrorActionPreference = 'Stop'
$lat = [Text.Encoding]::GetEncoding(28591)
$root = '<root>'
$bk = "$root\Pywel-Together\Backup_Code\$(Get-Date -Format 'dd-MM_HH-mm')_Personagem-proprio-do-guest_backup"
New-Item -ItemType Directory -Force $bk | Out-Null
$cpp = "$root\nativeserver\src\mod\session2\guest_save_host.cpp"
$dep = "$root\Pywel-Together\Source-Code\Deployer.cs"
Copy-Item $cpp $bk; Copy-Item $dep $bk
function Rpl($p, $old, $new) {
  $t = [IO.File]::ReadAllText($p, $lat)
  $n = ([regex]::Matches($t, [regex]::Escape($old))).Count
  if ($n -ne 1) { throw "$p : ancora achada $n vezes" }
  [IO.File]::WriteAllText($p, $t.Replace($old, $new), $lat)
}
# mod: com mais de uma pasta de conta, usa a da conta Steam logada (em vez de desligar o save proprio do guest)
Rpl $cpp @'
    if (found.size() != 1) {
'@.TrimEnd("`r","`n") @'
    if (found.size() > 1) {
        // Several account folders (saves of other accounts left on this PC): the one of the Steam account that is
        // logged in is the game's. Asked from the game's own steam_api64.dll.
        uint64_t steam_id = 0;
        if (HMODULE dll = GetModuleHandleW(L"steam_api64.dll")) {
            using UserFn = void* (*)();
            using IdFn = uint64_t (*)(void*);
            const auto user = reinterpret_cast<UserFn>(GetProcAddress(dll, "SteamAPI_SteamUser_v023"));
            const auto get_id = reinterpret_cast<IdFn>(GetProcAddress(dll, "SteamAPI_ISteamUser_GetSteamID"));
            if (user && get_id) {
                if (void* u = user()) steam_id = get_id(u);
            }
        }
        const std::wstring mine = std::to_wstring(static_cast<uint32_t>(steam_id & 0xFFFFFFFFull));
        if (steam_id != 0) {
            for (const auto& n : found) {
                if (n == mine) {
                    account = n;
                    return true;
                }
            }
        }
    }
    if (found.size() != 1) {
'@.TrimEnd("`r","`n")
# app: volta a deixar o save_guard do host como o modelo do mod (redirect); sem ele o guest vira clone do host
Rpl $dep @'
            // the game always saves in its ORIGINAL save folder; the app only makes copies (SaveBackup)
            if (role == Role.Host) ch |= Ini.Set(lines, "session2", "save_guard", "0");
'@.TrimEnd("`r","`n") ''
"ok -> $bk"
Get-Content "$root\porte-3732\build-r67.ps1"
