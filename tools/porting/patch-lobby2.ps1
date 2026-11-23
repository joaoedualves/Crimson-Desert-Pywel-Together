$ErrorActionPreference = 'Stop'
$ns = '<root>\nativeserver\src\mod'
$lat = [Text.Encoding]::GetEncoding(28591)

function PatchOne($file, $old, $new) {
  $raw = [IO.File]::ReadAllText($file, $lat)
  $crlf = $raw.Contains("`r`n")
  $t = if ($crlf) { $raw.Replace("`r`n", "`n") } else { $raw }
  $c = ([regex]::Matches($t, [regex]::Escape($old))).Count
  if ($c -ne 1) { throw "ancora achada $c vezes em $(Split-Path $file -Leaf): [$old]" }
  $t = $t.Replace($old, $new)
  if ($crlf) { $t = $t.Replace("`n", "`r`n") }
  [IO.File]::WriteAllText($file, $t, $lat)
  "ok: $(Split-Path $file -Leaf)"
}

$h = "$ns\net\steam_transport.h"
PatchOne $h '    void set_session_code(const std::string& code);' @'
    void set_session_code(const std::string& code);
    // Guest: the host found through the session code lobby (0 = not yet).
    uint64_t lobby_peer() const { return lobby_peer_; }
'@
PatchOne $h '    bool apply_limits();' @'
    bool apply_limits();
    void poll_lobby(uint32_t now_ms);
'@
PatchOne $h '    std::string session_key_;' @'
    std::string session_key_;
    enum class LobbyState { Idle, Busy };
    LobbyState lobby_state_ = LobbyState::Idle;
    uint64_t lobby_call_ = 0;
    uint64_t lobby_id_ = 0;       // host: the lobby we opened
    uint64_t joined_lobby_ = 0;   // guest: the lobby we joined
    uint64_t lobby_peer_ = 0;     // guest: the owner; host: the last member that joined
    uint32_t lobby_next_ms_ = 0;
'@

$m = "$ns\mod.cpp"
PatchOne $m '        net::SteamSendLimits limits;' @'
        if (cfg.steam_session_code.empty() && cfg.steam_peer_id == 0) {
            log::error("The Steam transport needs a session code ([steam] session_code) or a friend ([steam] "
                       "peer_steamid); the launcher fills one of them");
            net::cleanup();
            return;
        }
        net::SteamSendLimits limits;
'@
PatchOne $m @'
            const auto playing = steam->friends_playing();
            if (playing.size() == 1) {
'@ @'
            const uint64_t via_code = steam->lobby_peer();
            const auto playing = via_code ? std::vector<net::SteamFriend>{} : steam->friends_playing();
            if (via_code) {
                peer = net::PeerAddr::steam(via_code);
                steam->set_peer(via_code);
                log::info("Network: connecting through the session code (SteamID64 {})", via_code);
            } else if (playing.size() == 1) {
'@
"fim"
