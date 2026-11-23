$ErrorActionPreference = 'Stop'
$src = '<root>\Pywel-Together\Source-Code'
$enc = New-Object System.Text.UTF8Encoding($false)

function Patch($file, [scriptblock]$edit) {
    $p = Join-Path $src $file
    $t = [IO.File]::ReadAllText($p, $enc)
    $crlf = $t.Contains("`r`n")
    $t = $t -replace "`r`n", "`n"
    $t = & $edit $t
    if ($crlf) { $t = $t -replace "`n", "`r`n" }
    [IO.File]::WriteAllText($p, $t, $enc)
    "ok $file"
}
function Rep($t, $old, $new) {
    $i = $t.IndexOf($old)
    if ($i -lt 0) { throw "nao achei: $old" }
    if ($t.IndexOf($old, $i + 1) -ge 0) { throw "mais de uma vez: $old" }
    return $t.Replace($old, $new)
}

Patch 'MainForm.cs' {
    param($t)
    # campos
    $t = Rep $t 'TextBox txtHostName, txtGuestName, txtCode, txtLog, txtGameDir, txtManualIp, txtShareDir;' ('TextBox txtHostName, txtGuestName, txtCode, txtLog, txtGameDir, txtManualIp, txtShareDir;' + "`n" + '        CheckBox chkUdp; TextBox txtFriendId;')
    # host
    $t = Rep $t 'new Deployer.Overrides { PlayerName = cfg.HostName, PeerPort = cfg.PeerPort, Record = cfg.RecordPackets }' 'new Deployer.Overrides { PlayerName = cfg.HostName, PeerPort = cfg.PeerPort, Record = cfg.RecordPackets, Steam = cfg.UseSteam, PeerSteamId = cfg.FriendSteamId }'
    $t = Rep $t "                SaveCfg();`n                StartBeacon();" "                SaveCfg();`n                if (!cfg.UseSteam) StartBeacon();"
    $t = Rep $t 'AppLog.Write(Lang.T("host: passe o código ") + cfg.SessionCode + Lang.T(" para o guest"));' 'if (cfg.UseSteam) AppLog.Write(Lang.T("host: conexão pela Steam. O amigo entra pelo app dele (vocês precisam ser amigos na Steam)")); else AppLog.Write(Lang.T("host: passe o código ") + cfg.SessionCode + Lang.T(" para o guest"));'
    # guest
    $t = Rep $t "                if (string.IsNullOrWhiteSpace(cfg.SessionCode))`n                {`n                    if (string.IsNullOrWhiteSpace(cfg.ManualHostIp))" "                if (cfg.UseSteam)`n                {`n                    peer = `"`";`n                    AppLog.Write(Lang.T(`"guest: conexão pela Steam; o mod acha o amigo que está jogando (vocês precisam ser amigos na Steam)`"));`n                }`n                else if (string.IsNullOrWhiteSpace(cfg.SessionCode))`n                {`n                    if (string.IsNullOrWhiteSpace(cfg.ManualHostIp))"
    $t = Rep $t 'new Deployer.Overrides { PlayerName = cfg.GuestName, PeerHost = peer, PeerPort = remoteHost?.Port ?? cfg.PeerPort, Record = cfg.RecordPackets }' 'new Deployer.Overrides { PlayerName = cfg.GuestName, PeerHost = peer, PeerPort = remoteHost?.Port ?? cfg.PeerPort, Record = cfg.RecordPackets, Steam = cfg.UseSteam, PeerSteamId = cfg.FriendSteamId }'
    # config tab
    $t = Rep $t '            txtManualIp = T(cfg.ManualHostIp, 170);' ('            txtManualIp = T(cfg.ManualHostIp, 170);' + "`n" + '            txtFriendId = T(cfg.FriendSteamId, 170);' + "`n" + '            chkUdp = C(Lang.T("Usar rede local/VPN (Radmin, ZeroTier, Tailscale) em vez da Steam (opcional)"), !cfg.UseSteam);')
    $t = Rep $t '                Row(L(Lang.T("IP do host (opcional):")), txtManualIp, L(Lang.T("use para VPN (Radmin/ZeroTier/Tailscale) ou se a busca na rede não achar"), Dim, 9f)),' ('                L(Lang.T("Padrão: conexão pela Steam (como o mod original). Os dois precisam ser amigos na Steam."), Dim, 9f),' + "`n" + '                Row(L(Lang.T("SteamID64 do amigo (opcional):")), txtFriendId, L(Lang.T("vazio = acha sozinho o amigo da Steam que está jogando"), Dim, 9f)),' + "`n" + '                Row(chkUdp),' + "`n" + '                Row(L(Lang.T("IP do host (só no modo rede local/VPN):")), txtManualIp, L(Lang.T("use para VPN (Radmin/ZeroTier/Tailscale) ou se a busca na rede não achar"), Dim, 9f)),')
    # save
    $t = Rep $t '                cfg.ManualHostIp = txtManualIp.Text.Trim();' ('                cfg.ManualHostIp = txtManualIp.Text.Trim();' + "`n" + '                cfg.Transport = chkUdp.Checked ? "udp" : "steam";' + "`n" + '                cfg.FriendSteamId = new string(txtFriendId.Text.Where(char.IsDigit).ToArray());')
    return $t
}
"PATCH OK"
