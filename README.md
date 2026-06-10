# BlockHaven 3D

BlockHaven 3D est un jeu PC **solo offline** en C#/.NET 8 avec rendu OpenGL via OpenTK. Il ne dépend pas de Unity : GitHub Actions compile directement un vrai `.exe` Windows autonome.

## Ce qui est inclus

- Vue **vraiment 3D à la première personne** avec souris, ZQSD/WASD, sprint, saut/vol créatif.
- Monde voxel procédural avec forêt/nature, montagne, rivière, ville préfaite et blocs modifiables.
- Ville type Brookhaven au spawn : routes, lampadaires, maison starter, villas achetables, hôpital, police station, magasin.
- Zone aviation complète : terminal, entrepôt, hangar, piste, avion pilotable et décollage.
- Gameplay Minecraft-like : casser/poser des blocs, matériaux terre/pierre/bois/verre, inventaire et mode créatif/survie simple.
- Véhicules : voiture, moto, avion, entrée/sortie véhicule et trafic PNJ simple.
- PNJ : population locale, rôles (policier, médecin, commerçant, pilote, livreur), routine maison → travail → magasin → maison, humeur et argent simulé.
- Économie : argent local, salaire métier, maisons achetables, taxes et prix dynamiques simples.
- Sauvegarde locale exacte des modifications et états dans le dossier de l'exécutable :
  - `Saves/world.json`
  - `Saves/player.json`
  - `Saves/economy.json`
  - `Saves/npcs.json`
  - `Saves/vehicles.json`

## Contrôles

| Action | Touches |
| --- | --- |
| Avancer / reculer | `W`/`Z`, `S` |
| Gauche / droite | `A`/`Q`, `D` |
| Sprint | `Shift` |
| Monter / descendre en créatif | `Space`, `Ctrl` |
| Regarder | Souris |
| Casser un bloc | Clic gauche |
| Poser un bloc | Clic droit |
| Choisir bloc | `1` terre, `2` pierre, `3` bois, `4` verre |
| Entrer/sortir véhicule | `E` |
| Acheter maison proche | `H` |
| Recevoir salaire métier démo | `J` |
| Avion : demander décollage | `R` dans l'avion, puis accélérer sur la piste |
| Sauvegarder | `F5` |
| Pause / libérer souris | `Esc` |

## Télécharger le `.exe` via GitHub Actions

1. Pousse le dépôt sur GitHub.
2. Ouvre l'onglet **Actions**.
3. Lance ou attends le workflow **Build Windows EXE**.
4. Télécharge l'artifact **BlockHaven3D-windows-x64**.
5. Dézippe puis lance `BlockHaven3D.exe` sur Windows.

Le workflow publie un binaire `win-x64` self-contained single-file, donc aucun serveur et aucune connexion Internet ne sont nécessaires pour jouer une fois l'artifact téléchargé.

## Build local Windows

```powershell
dotnet restore BlockHaven3D.sln
dotnet publish src/BlockHaven3D/BlockHaven3D.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o Builds/Windows
```

L'exécutable sera dans `Builds/Windows/BlockHaven3D.exe`.

## Architecture

- `BlockHavenGame` : boucle principale, entrée clavier/souris, rendu FPS 3D.
- `WorldManager` : monde voxel, ville, airport, maisons, raycast blocs et persistance des modifications.
- `SaveManager` : sauvegardes JSON offline dans `Saves/`.
- `NpcManager` : routines PNJ et simulation ville.
- `EconomyManager` : argent, achats, salaires et taxes.
- `Vehicle` : voiture/moto/avion, conduite et décollage.
- `ShaderProgram` / `CubeRenderer` : rendu OpenGL simple avec lumière jour/nuit.
