$ErrorActionPreference = 'Stop'
$root = '<root>'
$ns = "$root\nativeserver\src\mod"
$app = "$root\Pywel-Together\Source-Code"
$lat = [Text.Encoding]::GetEncoding(28591)

# backup
$stamp = Get-Date -Format 'dd-MM_HH-mm'
$bk = "$root\Pywel-Together\Backup_Code\${stamp}_Sessao-Steam_backup"
New-Item -ItemType Directory -Force "$bk\01.Source_code\mod","$bk\01.Source_code\app","$bk\02.DLL" | Out-Null
robocopy "$root\nativeserver\src" "$bk\01.Source_code\mod" /E /NFL /NDL /NJH /NJS | Out-Null
robocopy $app "$bk\01.Source_code\app" /E /NFL /NDL /NJH /NJS | Out-Null
Copy-Item 'F:\SteamLibrary\steamapps\common\Crimson Desert\bin64\cdmp.asi' "$bk\02.DLL\" -EA SilentlyContinue
"backup: $bk"

function Patch($file, $pairs) {
  $raw = [IO.File]::ReadAllText($file, $lat)
  $crlf = $raw.Contains("`r`n")
  $t = if ($crlf) { $raw.Replace("`r`n", "`n") } else { $raw }
  foreach ($p in $pairs) {
    $old = $p[0]; $new = $p[1]
    $c = ([regex]::Matches($t, [regex]::Escape($old))).Count
    if ($c -ne 1) { throw "ancora achada $c vezes em $(Split-Path $file -Leaf): $($old.Substring(0,[math]::Min(60,$old.Length)))" }
    $t = $t.Replace($old, $new)
  }
  if ($crlf) { $t = $t.Replace("`n", "`r`n") }
  [IO.File]::WriteAllText($file, $t, $lat)
  "ok: $(Split-Path $file -Leaf)"
}

# ---- steam_transport.cpp
Patch "$ns\net\steam_transport.cpp" @(
  ,@('if (!playing || (info.game_id & 0x100077F) != kAppId) continue;
        const char* name = nullptr;',
     'if (!playing || (info.game_id & 0xFFFFFFull) != kAppId) continue;
        // Session code: only friends that publish the same code in their rich
        // presence are our peer (set in become_ready from [steam] session_code).
        if (!session_key_.empty() && a.friend_rich) {
            const char* v = nullptr;
            call(v, a.friend_rich, a.frn, id, "cdmp_session");
            if (!v || session_key_ != v) continue;
        }
        const char* name = nullptr;')
  ,@('bool (*friend_game)(void*, uint64_t, FriendGameInfo*) = nullptr;
',
     'bool (*friend_game)(void*, uint64_t, FriendGameInfo*) = nullptr;
    // Optional (session code pairing through rich presence)
    bool (*set_rich)(void*, const char*, const char*) = nullptr;
    const char* (*friend_rich)(void*, uint64_t, const char*) = nullptr;
')
  ,@('    a.iterate_marker = reinterpret_cast<void*>(',
     '    a.set_rich = reinterpret_cast<decltype(a.set_rich)>(
        GetProcAddress(dll, "SteamAPI_ISteamFriends_SetRichPresence"));
    a.friend_rich = reinterpret_cast<decltype(a.friend_rich)>(
        GetProcAddress(dll, "SteamAPI_ISteamFriends_GetFriendRichPresence"));
    a.iterate_marker = reinterpret_cast<void*>(')
  ,@('    const auto playing = friends_playing();
    if (playing.empty()) {
        log::info("Steam: no friends are playing Crimson Desert right now");
    }',
     '    if (!session_key_.empty()) {
        bool ok = false;
        if (a.set_rich && call(ok, a.set_rich, a.frn, "cdmp_session", session_key_.c_str()) && ok) {
            log::info("Steam: session code published to friends (rich presence)");
        } else {
            log::warn("Steam: could not publish the session code; any friend playing will be accepted");
            session_key_.clear();
        }
    }
    const auto playing = friends_playing();
    if (playing.empty()) {
        log::info("Steam: no friends are playing Crimson Desert with this session code yet");
    }')
  ,@('std::vector<SteamFriend> SteamTransport::friends_playing() {',
     'void SteamTransport::set_session_code(const std::string& code) {
    std::string up;
    for (char c : code) {
        if (c == '' '' || c == ''\t'') continue;
        up += (c >= ''a'' && c <= ''z'') ? static_cast<char>(c - 32) : c;
    }
    if (up.empty()) {
        session_key_.clear();
        return;
    }
    uint64_t h = 1469598103934665603ull;  // FNV-1a
    for (char c : std::string("cdmp-pywel:") + up) {
        h ^= static_cast<uint8_t>(c);
        h *= 1099511628211ull;
    }
    session_key_ = std::format("{:016x}", h);
}

std::vector<SteamFriend> SteamTransport::friends_playing() {')
)

# ---- steam_transport.h
Patch "$ns\net\steam_transport.h" @(
  ,@('    void set_peer(uint64_t id) { peer_ = id; }
',
     '    void set_peer(uint64_t id) { peer_ = id; }
    // Session code (the launcher''s password): published in rich presence and
    // required from every friend we pair with. Empty = any friend playing.
    void set_session_code(const std::string& code);
')
  ,@('    std::unique_ptr<Api> api_;
    int channel_;',
     '    std::unique_ptr<Api> api_;
    std::string session_key_;
    int channel_;')
)

# ---- config.h / config.cpp
Patch "$ns\config.h" @(
  ,@('    uint64_t steam_peer_id = 0;
',
     '    uint64_t steam_peer_id = 0;
    // [steam] session_code: the launcher''s session code; friends with the same code pair up on their own.
    std::string steam_session_code;
')
)
Patch "$ns\config.cpp" @(
  ,@('    c.steam_channel = static_cast<int>(read_uint(ini_path, L"steam", L"channel", 67));',
     '    c.steam_session_code = narrow(read_string(ini_path, L"steam", L"session_code", ""));
    c.steam_channel = static_cast<int>(read_uint(ini_path, L"steam", L"channel", 67));')
  ,@('    L"peer_steamid=\r\n"
',
     '    L"peer_steamid=\r\n"
    L"; Same code on both PCs (the launcher fills it): friends with the same code pair up automatically\r\n"
    L"session_code=\r\n"
')
)

# ---- mod.cpp
Patch "$ns\mod.cpp" @(
  ,@('        steam = st.get();
        transport = std::move(st);',
     '        st->set_session_code(cfg.steam_session_code);
        steam = st.get();
        transport = std::move(st);')
  ,@('"Network: {} friends playing; put the host''s SteamID64 in [steam] "
                          "peer_steamid (listed above when Steam got ready)"',
     '"Network: {} friends playing with this session code; waiting for exactly one "
                          "(use a different session code for each group)"')
)

# ---- launcher
Patch "$app\Deployer.cs" @(
  ,@('public string PeerSteamId = "";',
     'public string SessionCode = ""; public string PeerSteamId = "";')
  ,@('if (o.Steam) ch |= Ini.Set(lines, "steam", "peer_steamid", Regex.Replace(o.PeerSteamId ?? "", @"\D", ""));',
     'if (o.Steam) ch |= Ini.Set(lines, "steam", "peer_steamid", Regex.Replace(o.PeerSteamId ?? "", @"\D", ""));
            if (o.Steam) ch |= Ini.Set(lines, "steam", "session_code", (o.SessionCode ?? "").Trim().ToUpperInvariant());')
)
$mf = "$app\MainForm.cs"
$raw = [IO.File]::ReadAllText($mf, $lat)
$n1 = ([regex]::Matches($raw, [regex]::Escape('Steam = cfg.UseSteam, PeerSteamId = cfg.FriendSteamId'))).Count
if ($n1 -ne 2) { throw "MainForm: esperava 2 ocorrencias, achei $n1" }
$raw = $raw.Replace('Steam = cfg.UseSteam, PeerSteamId = cfg.FriendSteamId', 'Steam = cfg.UseSteam, PeerSteamId = cfg.FriendSteamId, SessionCode = cfg.SessionCode')
[IO.File]::WriteAllText($mf, $raw, $lat)
"ok: MainForm.cs (2x)"
