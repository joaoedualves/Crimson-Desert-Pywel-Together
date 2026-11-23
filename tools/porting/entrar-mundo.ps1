$log = 'F:\SteamLibrary\steamapps\common\Crimson Desert\bin64\cdmp.host.log'
$ws = New-Object -ComObject WScript.Shell
for ($i = 0; $i -lt 14; $i++) {
  Start-Sleep 15
  $t = Get-Content $log -Tail 40 | Out-String
  if ($t -match 'Local player: actor|Local: pos|controlled actor 0x') { "no mundo ($i)"; break }
  $p = Get-Process CrimsonDesert -EA 0 | Select-Object -First 1
  if (-not $p) { 'jogo fechado'; break }
  if ($ws.AppActivate($p.Id)) { Start-Sleep -Milliseconds 400; $ws.SendKeys('e'); "E enviado ($i)" } else { "sem foco ($i)" }
}
