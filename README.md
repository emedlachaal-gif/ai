# BlockHaven 3D

BlockHaven 3D est un jeu PC **solo offline** en C#/.NET 8 avec rendu OpenGL via OpenTK. Il ne dépend pas de Unity : GitHub Actions compile directement un vrai `.exe` Windows autonome.

## Ce qui est inclus

- Vue **vraiment 3D à la première personne** avec souris, ZQSD/WASD, sprint, gravité, chute et vol uniquement en mode créatif.
- Monde voxel procédural avec forêt/nature, montagne, rivière, ville préfaite et blocs modifiables.
- Ville type Brookhaven au spawn : routes, lampadaires, maison starter, villas achetables, hôpital, police station, magasin et portes animées ouvrables.
- Zone aviation complète : terminal vitré, entrepôt avec rampe, hangar avec porte, piste très longue avec marquages, balises lumineuses et avion pilotable en vue cockpit.
- Gameplay Minecraft-like : casser/poser des blocs, matériaux terre/pierre/bois/verre, inventaire et mode créatif/survie simple.
- Véhicules : voiture, moto, avion, entrée/sortie véhicule et trafic PNJ simple.
- PNJ : population locale, rôles (policier, médecin, commerçant, pilote, livreur), routine maison → travail → magasin → maison, humeur et argent simulé.
- Économie : argent local, salaire métier, maisons achetables, taxes et prix dynamiques simples.
- Graphismes style **Brookhaven / Roblox** avancés : shader Blinn-Phong/toon avec rim lighting dynamique, AO voxel directionnelle, matériaux procéduraux (plastique brillant, asphalte rugueux, bois veiné, béton mat), eau avec vagues animées, sky gradient aube/zénith/crépuscule/nuit, lune/étoiles simulées, brouillard exponentiel, contours noirs doux, PNJ détaillés façon R15, routes avec marquages, trottoirs, passages piétons, panneaux colorés et fontaine.
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
| Sauter / gravité | `Space` en survie |
| Monter / descendre en créatif | `Space`, `Ctrl` |
| Regarder | Souris |
| Casser un bloc | Clic gauche |
| Poser un bloc | Clic droit |
| Choisir bloc | `1` terre, `2` pierre, `3` bois, `4` verre |
| Ouvrir/fermer porte proche | `E` près d’une porte |
| Entrer/sortir véhicule | `E` près d’un véhicule |
| Acheter maison proche | `H` |
| Recevoir salaire métier démo | `J` |
| Avion : turbines / freinage | `Z`/`W` pour accélérer, `S` pour freiner |
| Avion : lacet | `Q`/`A`, `D` |
| Avion : tangage | `Space`/`↑` pour lever le nez, `Shift`/`↓` pour baisser |
| Avion : roulis | `←`, `→` |
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

- `BlockHavenGame` : boucle principale, entrée clavier/souris, rendu FPS 3D, shader Blinn-Phong/toon, sky gradient, fog exponentiel, vue cockpit, head bobbing et modèles PNJ/véhicules avec contours.
- `WorldManager` : monde voxel, ville Brookhaven-like, détails de rue Roblox, airport, maisons, portes animées, raycast blocs et persistance des modifications.
- `SaveManager` : sauvegardes JSON offline dans `Saves/`.
- `NpcManager` : routines PNJ et simulation ville.
- `EconomyManager` : argent, achats, salaires et taxes.
- `Vehicle` : voiture/moto/avion, cockpit, vitesse de portance, tangage, roulis, lacet, freinage et décollage conditionnel.
- `ShaderProgram` / `CubeRenderer` : rendu OpenGL simple avec lumière jour/nuit.
