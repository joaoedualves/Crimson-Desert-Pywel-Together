$ErrorActionPreference = 'Stop'
$root = '<root>'
$ns = "$root\nativeserver\src\mod"
$lat = [Text.Encoding]::GetEncoding(28591)

$stamp = Get-Date -Format 'dd-MM_HH-mm'
$bk = "$root\Pywel-Together\Backup_Code\${stamp}_Lobby-senha_backup"
New-Item -ItemType Directory -Force "$bk\01.Source_code\mod" | Out-Null
robocopy "$root\nativeserver\src" "$bk\01.Source_code\mod" /E /NFL /NDL /NJH /NJS | Out-Null
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

# ---------------- steam_transport.cpp
$apiOld = @'
    // Optional (session code pairing through rich presence)
    bool (*set_rich)(void*, const char*, const char*) = nullptr;
'@
$apiNew = @'
    // Optional (session code lobbies: ISteamMatchmaking + ISteamUtils)
    void* (*matchmaking)() = nullptr;
    void* (*steam_utils)() = nullptr;
    uint64_t (*mm_create)(void*, int, int) = nullptr;
    uint64_t (*mm_request)(void*) = nullptr;
    void (*mm_filter_str)(void*, const char*, const char*, int) = nullptr;
    void (*mm_filter_dist)(void*, int) = nullptr;
    void (*mm_filter_count)(void*, int) = nullptr;
    uint64_t (*mm_by_index)(void*, int) = nullptr;
    uint64_t (*mm_owner)(void*, uint64_t) = nullptr;
    bool (*mm_set_data)(void*, uint64_t, const char*, const char*) = nullptr;
    uint64_t (*mm_join)(void*, uint64_t) = nullptr;
    void (*mm_leave)(void*, uint64_t) = nullptr;
    int (*mm_num_members)(void*, uint64_t) = nullptr;
    uint64_t (*mm_member)(void*, uint64_t, int) = nullptr;
    bool (*call_done)(void*, uint64_t, bool*) = nullptr;
    bool (*call_result)(void*, uint64_t, void*, int, int, bool*) = nullptr;
    void* mm = nullptr;
    void* sutil = nullptr;
    // Optional (session code pairing through rich presence)
    bool (*set_rich)(void*, const char*, const char*) = nullptr;
'@

$resOld = @'
    a.set_rich = reinterpret_cast<decltype(a.set_rich)>(
'@
$resNew = @'
    auto opt = [&](auto& fn, const char* name) {
        fn = reinterpret_cast<std::remove_reference_t<decltype(fn)>>(GetProcAddress(dll, name));
    };
    opt(a.matchmaking, "SteamAPI_SteamMatchmaking_v009");
    opt(a.steam_utils, "SteamAPI_SteamUtils_v010");
    opt(a.mm_create, "SteamAPI_ISteamMatchmaking_CreateLobby");
    opt(a.mm_request, "SteamAPI_ISteamMatchmaking_RequestLobbyList");
    opt(a.mm_filter_str, "SteamAPI_ISteamMatchmaking_AddRequestLobbyListStringFilter");
    opt(a.mm_filter_dist, "SteamAPI_ISteamMatchmaking_AddRequestLobbyListDistanceFilter");
    opt(a.mm_filter_count, "SteamAPI_ISteamMatchmaking_AddRequestLobbyListResultCountFilter");
    opt(a.mm_by_index, "SteamAPI_ISteamMatchmaking_GetLobbyByIndex");
    opt(a.mm_owner, "SteamAPI_ISteamMatchmaking_GetLobbyOwner");
    opt(a.mm_set_data, "SteamAPI_ISteamMatchmaking_SetLobbyData");
    opt(a.mm_join, "SteamAPI_ISteamMatchmaking_JoinLobby");
    opt(a.mm_leave, "SteamAPI_ISteamMatchmaking_LeaveLobby");
    opt(a.mm_num_members, "SteamAPI_ISteamMatchmaking_GetNumLobbyMembers");
    opt(a.mm_member, "SteamAPI_ISteamMatchmaking_GetLobbyMemberByIndex");
    opt(a.call_done, "SteamAPI_ISteamUtils_IsAPICallCompleted");
    opt(a.call_result, "SteamAPI_ISteamUtils_GetAPICallResult");
    a.set_rich = reinterpret_cast<decltype(a.set_rich)>(
'@

$readyOld = @'
    if (!session_key_.empty()) {
        bool ok = false;
        if (a.set_rich
'@
$readyNew = @'
    if (a.matchmaking) call(a.mm, a.matchmaking);
    if (a.steam_utils) call(a.sutil, a.steam_utils);
    if (!session_key_.empty()) {
        bool ok = false;
        if (a.set_rich
'@

$pollOld = @'
    int relay = 0;
    if (!call(relay, a.relay_status, a.utl, static_cast<void*>(nullptr))) return fail("GetRelayNetworkStatus");
'@
$pollNew = @'
    poll_lobby(now_ms);
    if (disabled_) return;

    int relay = 0;
    if (!call(relay, a.relay_status, a.utl, static_cast<void*>(nullptr))) return fail("GetRelayNetworkStatus");
'@

$lobbyFn = @'
// Session code pairing without friendship: the host opens a public lobby
// that carries the hashed code, the guest searches lobbies with that exact
// value and takes the lobby owner as its peer. The guest also joins the lobby
// so the host sees who it is and accepts the Steam networking session.
void SteamTransport::poll_lobby(uint32_t now_ms) {
    Api& a = *api_;
    if (session_key_.empty() || !a.mm || !a.sutil || !a.mm_create || !a.mm_request || !a.call_done ||
        !a.call_result || !a.mm_filter_str || !a.mm_by_index || !a.mm_owner || !a.mm_set_data || !a.mm_join ||
        !a.mm_num_members || !a.mm_member) {
        return;
    }
    constexpr int kLobbyMatchListCb = 510;  // k_iSteamMatchmakingCallbacks + 10
    constexpr int kLobbyCreatedCb = 513;    // k_iSteamMatchmakingCallbacks + 13
    constexpr int kLobbyTypePublic = 2;
    constexpr int kDistanceWorldwide = 3;
    constexpr int kComparisonEqual = 0;
    // 1 = finished (ok says whether the result was read), 0 = still running.
    auto finished = [&](uint64_t h, int cb_id, void* buf, int size, bool& ok) -> int {
        bool failed = false, done = false;
        if (!call(done, a.call_done, a.sutil, h, &failed)) {
            fail("IsAPICallCompleted");
            return 0;
        }
        if (!done) return 0;
        ok = false;
        if (!failed) {
            bool failed2 = false, got = false;
            if (!call(got, a.call_result, a.sutil, h, buf, size, cb_id, &failed2)) {
                fail("GetAPICallResult");
                return 0;
            }
            ok = got && !failed2;
        }
        return 1;
    };

    if (host_) {
        if (lobby_id_ == 0) {
            if (lobby_state_ == LobbyState::Idle && now_ms >= lobby_next_ms_) {
                uint64_t h = 0;
                if (!call(h, a.mm_create, a.mm, kLobbyTypePublic, 4)) return fail("CreateLobby");
                lobby_call_ = h;
                lobby_state_ = LobbyState::Busy;
            } else if (lobby_state_ == LobbyState::Busy) {
                struct Created {
                    int32_t result;
                    int32_t pad;
                    uint64_t lobby;
                } r{};
                bool ok = false;
                if (finished(lobby_call_, kLobbyCreatedCb, &r, static_cast<int>(sizeof r), ok)) {
                    lobby_state_ = LobbyState::Idle;
                    lobby_next_ms_ = now_ms + 5000;
                    if (ok && r.result == 1 && r.lobby) {
                        lobby_id_ = r.lobby;
                        bool set = false;
                        call(set, a.mm_set_data, a.mm, lobby_id_, "cdmp_session", session_key_.c_str());
                        log::info("Steam: session lobby open for this code (waiting for the guest)");
                    } else {
                        log::warn("Steam: could not open the session lobby (result {}); trying again", r.result);
                    }
                }
            }
        } else {
            int n = 0;
            if (!call(n, a.mm_num_members, a.mm, lobby_id_)) return fail("GetNumLobbyMembers");
            for (int i = 0; i < n && i < 8; ++i) {
                uint64_t m = 0;
                if (!call(m, a.mm_member, a.mm, lobby_id_, i)) return fail("GetLobbyMemberByIndex");
                if (!m || m == own_id_) continue;
                const auto id = identity(m);
                bool ok = false;
                if (!call(ok, a.accept, a.msgs, &id)) return fail("AcceptSessionWithUser");
                if (m != lobby_peer_) {
                    lobby_peer_ = m;
                    log::info("Steam: SteamID64 {} joined the session lobby", m);
                }
            }
        }
        return;
    }

    // Guest
    if (peer_ != 0 || lobby_peer_ != 0) return;
    if (lobby_state_ == LobbyState::Idle && now_ms >= lobby_next_ms_) {
        if (!call_void(a.mm_filter_str, a.mm, "cdmp_session", session_key_.c_str(), kComparisonEqual)) {
            return fail("AddRequestLobbyListStringFilter");
        }
        if (a.mm_filter_dist) call_void(a.mm_filter_dist, a.mm, kDistanceWorldwide);
        if (a.mm_filter_count) call_void(a.mm_filter_count, a.mm, 10);
        uint64_t h = 0;
        if (!call(h, a.mm_request, a.mm)) return fail("RequestLobbyList");
        lobby_call_ = h;
        lobby_state_ = LobbyState::Busy;
    } else if (lobby_state_ == LobbyState::Busy) {
        uint32_t n = 0;
        bool ok = false;
        if (finished(lobby_call_, kLobbyMatchListCb, &n, static_cast<int>(sizeof n), ok)) {
            lobby_state_ = LobbyState::Idle;
            lobby_next_ms_ = now_ms + 3000;
            if (ok && n > 0) {
                for (uint32_t i = 0; i < n && i < 10; ++i) {
                    uint64_t lobby = 0, owner = 0;
                    if (!call(lobby, a.mm_by_index, a.mm, static_cast<int>(i))) return fail("GetLobbyByIndex");
                    if (!lobby) continue;
                    if (!call(owner, a.mm_owner, a.mm, lobby)) return fail("GetLobbyOwner");
                    if (!owner || owner == own_id_) continue;
                    lobby_peer_ = owner;
                    uint64_t j = 0;
                    call(j, a.mm_join, a.mm, lobby);
                    joined_lobby_ = lobby;
                    log::info("Steam: found the host with this session code (SteamID64 {})", owner);
                    break;
                }
            }
        }
    }
}

void SteamTransport::set_session_code(const std::string& code) {
'@
$lobbyFnOld = "void SteamTransport::set_session_code(const std::string& code) {"

$dtorOld = @'
SteamTransport::~SteamTransport() {
'@
$dtorNew = @'
SteamTransport::~SteamTransport() {
    if (ready_ && !disabled_ && api_->mm && api_->mm_leave) {
        if (lobby_id_) call_void(api_->mm_leave, api_->mm, lobby_id_);
        if (joined_lobby_) call_void(api_->mm_leave, api_->mm, joined_lobby_);
    }
'@

Patch "$ns\net\steam_transport.cpp" @(
  ,@($apiOld, $apiNew)
  ,@($resOld, $resNew)
  ,@($readyOld, $readyNew)
  ,@($pollOld, $pollNew)
  ,@($lobbyFnOld, $lobbyFn)
  ,@($dtorOld, $dtorNew)
)

# ---------------- steam_transport.h
$hOld = @'
    void set_session_code(const std::string& code);
'@
$hNew = @'
    void set_session_code(const std::string& code);
    // Guest: the host found through the session code lobby (0 = not yet).
    uint64_t lobby_peer() const { return lobby_peer_; }
'@
$hOld2 = @'
    bool apply_limits();
'@
$hNew2 = @'
    bool apply_limits();
    void poll_lobby(uint32_t now_ms);
'@
$hOld3 = @'
    std::string session_key_;
'@
$hNew3 = @'
    std::string session_key_;
    enum class LobbyState { Idle, Busy };
    LobbyState lobby_state_ = LobbyState::Idle;
    uint64_t lobby_call_ = 0;
    uint64_t lobby_id_ = 0;       // host: the lobby we opened
    uint64_t joined_lobby_ = 0;   // guest: the lobby we joined
    uint64_t lobby_peer_ = 0;     // guest: the owner; host: the last member that joined
    uint32_t lobby_next_ms_ = 0;
'@
Patch "$ns\net\steam_transport.h" @( ,@($hOld,$hNew) ,@($hOld2,$hNew2) ,@($hOld3,$hNew3) )

# ---------------- mod.cpp
$m1Old = @'
        net::SteamSendLimits limits;
'@
$m1New = @'
        if (cfg.steam_session_code.empty() && cfg.steam_peer_id == 0) {
            log::error("The Steam transport needs a session code ([steam] session_code) or a friend ([steam] "
                       "peer_steamid); the launcher fills one of them");
            net::cleanup();
            return;
        }
        net::SteamSendLimits limits;
'@
$m2Old = @'
            const auto playing = steam->friends_playing();
            if (playing.size() == 1) {
'@
$m2New = @'
            const uint64_t via_code = steam->lobby_peer();
            const auto playing = via_code ? std::vector<net::SteamFriend>{} : steam->friends_playing();
            if (via_code) {
                peer = net::PeerAddr::steam(via_code);
                steam->set_peer(via_code);
                log::info("Network: connecting through the session code (SteamID64 {})", via_code);
            } else if (playing.size() == 1) {
'@
Patch "$ns\mod.cpp" @( ,@($m1Old,$m1New) ,@($m2Old,$m2New) )
"fim"
