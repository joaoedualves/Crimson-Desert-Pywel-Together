# Crimson Desert: Pywel Together

## What is this?

Crimson Desert is a single player game. **Pywel Together** lets two friends play it together online: one player opens their game as the **host**, and the other (the **guest**) enters the host's world with their own character. Both walk around the same map and see each other move, with the same people and enemies around them.

You don't need to edit any game file by hand. Open the **Pywel Together** app, choose **Host** or **Guest**, type the same session code on both PCs and the app opens the game, loads the save and puts the guest inside the host's world. Each player keeps their own character and their own story progress.

It's a fan project made by two friends who wanted to play together, built on top of the co-op research of **ByJunter** (see Credits). It is still being tested, so expect bugs.

## How it works

Online co-op for **Crimson Desert** (PC, Steam), made of two parts:

- **The mod** (`cdmp.asi`, loaded by Ultimate ASI Loader): the guest joins the host's world through the game's own internal server. Same world, same NPCs and enemies.
- **The launcher** (`launcher/`, Windows app "Pywel Together"): HOST and GUEST buttons that start the game in the right role, find the host by a session code, copy the mod files from the host when they differ, restore files removed by antivirus, manage saves and clean logs.

> **Status: provisional and still in testing.** This was made for our own sessions while the game has no official co-op. It is **not** a maintained project. Anyone who wants to continue or improve it is welcome to (see License).

## Credits

This project is built on the work of **ByJunter**:

- [CrimsonDesertMultiplayer](https://github.com/ByJunter/CrimsonDesertMultiplayer)
- [CrimsonDesertMultiplayer-NativeServer](https://github.com/ByJunter/CrimsonDesertMultiplayer-NativeServer) (the original README is kept in `README-NativeServer-ByJunter.md`)

## What we changed

- Ported the NativeServer mod from game build 1.0.0.2976 to **1.0.0.3663** (addresses, signatures and packet layouts).
- Smoother movement of the other player (adaptive interpolation, empty movement packets refilled on the host).
- Guest no longer stays frozen after reviving at a save point, and the loading after death is shorter.
- New launcher app.

## Known limits

- Works only with game build **1.0.0.3663**. A Steam update can break it.
- **One guest** for now.
- Same local network works. Over the internet only through a VPN that gives both PCs a shared network (Radmin, ZeroTier, Tailscale): **not tested yet**.
- Not tested yet: horse/mounts for the guest, enemies in both directions, combat.
- Back up your saves before using it.

## Building

- Mod: CMake + Visual Studio 2022, target `cdmp_mod` (see `CMakeLists.txt`).
- Launcher: .NET, `launcher/build.ps1`.

## License

GPL-3.0, same as the original projects (see `LICENSE`).

---

## Português

Co-op online provisório para Crimson Desert: o mod (guest entra no mundo do host) e o app launcher "Pywel Together". Feito para jogarmos enquanto o jogo não tem co-op oficial. **Não é um projeto mantido**; quem quiser continuar ou melhorar, fique à vontade. Ainda em teste: só funciona no build 1.0.0.3663, um guest por vez, VPN, cavalo, inimigos e combate ainda sem teste. Créditos ao **ByJunter** pelos projetos originais.