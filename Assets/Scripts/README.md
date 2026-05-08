# Multiplayer Boss Battle MVP - PUN 2 Setup

This project now uses **PUN 2 (Photon Unity Networking 2)** instead of Unity Netcode for GameObjects.

## 1. Install PUN 2

PUN 2 is not installed through `Packages/manifest.json`.

1. Open **Window > Asset Store** or the Unity Asset Store web page.
2. Find **PUN 2 - FREE**.
3. Import it into this Unity project.
4. Create a free Photon app at [dashboard.photonengine.com](https://dashboard.photonengine.com).
5. Copy the Photon **App ID**.
6. In Unity, open the PUN setup wizard and paste the App ID.

After import, these namespaces should exist:

- `Photon.Pun`
- `Photon.Realtime`
- `ExitGames.Client.Photon`

## 2. Build the scene and prefabs

After PUN 2 is imported and the project compiles:

1. Open your main scene.
2. Run **Tools > Boss Battle MVP > Build Photon Everything**.
3. Save the scene.

The setup script creates:

- `Assets/Resources/Player.prefab`
- `Assets/Resources/Boss.prefab`
- `Assets/Resources/BossProjectile.prefab`
- `Assets/Materials/BossWhite.mat`
- `Assets/Materials/ProjectileRed.mat`
- `Assets/Materials/PlayerBlue.mat`
- A scene `GameManager` with `NetworkGameManager` and `ArenaBuilder`
- A scene `MenuCanvas` with `MultiplayerMenuUI`

PUN requires network-spawned prefabs to live in a `Resources` folder. Do not move these prefabs out of `Assets/Resources/` unless you also change the prefab names in `NetworkGameManager`, `NetworkBossAttack`, and `PhotonNetwork.Instantiate` calls.

## 3. How multiplayer works now

- **Host** creates a Photon room named `BossRoom`.
- **Join** joins that same room.
- There is no IP input because Photon Cloud handles routing.
- The first player in the room is the **MasterClient**.
- The MasterClient is the authority for:
  - Boss spawning
  - Boss health
  - Boss attacks
  - Projectile spawning
  - Projectile damage decisions

## 4. Testing

1. Run one game instance and click **Host**.
2. Run another game instance and click **Join**.
3. Both must use the same room name, default `BossRoom`.

For local testing:

- Use one Unity Editor and one desktop build, or
- Use two desktop builds, or
- Use ParrelSync / Multiplayer Play Mode if you have it installed.

## 5. Controls

- WASD: move
- Mouse: look
- Left click: shoot
- R: reload
- Space: jump

## 6. Current defaults

| Setting | Value |
|---|---|
| Player health | 100 |
| Boss health | 1000 |
| Gun damage | 25 |
| Magazine size | 12 |
| Reload time | 1.5 seconds |
| Fire rate | 0.2 seconds |
| Boss attack interval | 2 seconds |
| Boss projectile damage | 10 |
| Boss projectile speed | 10 |
| Arena size | 30 x 30 |
| Player spawn points | 4 |

## 7. Common Photon issues

### `The name Photon does not exist`

PUN 2 has not been imported yet. Import **PUN 2 - FREE** from the Asset Store, then enter your App ID.

### Players do not spawn

Make sure `Player.prefab` is inside `Assets/Resources/` and that its exact prefab name is `Player`.

### Boss does not spawn

Make sure `Boss.prefab` is inside `Assets/Resources/` and that its exact prefab name is `Boss`.

### Projectiles do not spawn

Make sure `BossProjectile.prefab` is inside `Assets/Resources/` and that its exact prefab name is `BossProjectile`.

### Two cameras or two AudioListeners

Delete the default scene camera. The setup script removes it automatically. Each player prefab owns its own camera, and the scripts enable it only for the local `photonView.IsMine` player.

### Old Netcode errors

Unity Netcode for GameObjects and Unity Transport were removed from `Packages/manifest.json`. If you still see old NGO components on scene objects, delete them or rerun **Tools > Boss Battle MVP > Build Photon Everything** after PUN is imported.
