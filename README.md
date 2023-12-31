# RimworldDoorstop

Companion implementation to [Unity Doorstop](https://github.com/NeighTools/UnityDoorstop).

# Why use it

The reason this project exists is that RimWorld loads mods by first loading their dll into a byte[] and then load the bytes as an assembly. This usually breaks the relation between the dll and its pdb file and as a result breakpoints won't be active and useable. The code in the repository fixes this by patching the load mechanics with Harmony so the dll's are loaded by pointing out the dll file. 

# How to use

First, install Unity Doorstop by downloading their latest release and put the file `winhttp.dll` and `doorstop_config.ini` in the root directory of RimWorld. The default location of the directory is usually `C:\Program Files (x86)\Steam\steamapps\common\RimWorld`.

Next, compile this project or download the release to get `Doorstop.dll` and the lastest `0Harmony.dll` which you also put into the games root directly.

You should now have the contents of the RimWorld root directory look like this:
```
Data
Mods
MonoBleedingEdge
RimWorldWin64_Data
Source
0Harmony.dll          <== ADDED BY THIS REPOSITORY
Doorstop.dll          <== ADDED BY THIS REPOSITORY
doorstop_config.ini   <== ADDED BY UNITY DOORSTOP
EULA.txt
Licenses.txt
ModUpdating.txt
Readme.txt
RimWorldWin64.exe
ScenarioPreview.jpg
steam_appid.txt
SteamInputDefaultConfiguration.vdf
SteamInputDefaultConfiguration_SteamDeck.vdf
UnityCrashHandler64.exe
UnityPlayer.dll
Version.txt
winhttp.dll           <== ADDED BY UNITY DOORSTOP
doorstop_a6b2ef3.log  <== ADDED EACH TIME YOU START THE GAME
```

Finally, you need to adjust the `doorstop_config.ini` file a bit. Here is how I define it:
```
[General]
enabled=true
target_assembly=Doorstop.dll
redirect_output_log=false
ignore_disable_switch=true

[UnityMono]
dll_search_path_override=
debug_enabled=true
debug_address=127.0.0.1:56000
debug_suspend=false
```

Verify that when you start the game, it should output something similar to this at the beginning of the game log:
```
Patching the base game from doorstop
RimWorld 1.4.3901 rev238
Loading mod MODNAME1
- loaded ...
Loading mod MODNAME2
- loaded ...
....
```

Important: Make sure you compile your mod in `Debug` mode so you get `ModName.dll` and `ModName.pdb` into the Mod folder (and please don't release your mod like this!). 

Now start the game, then use [dnSpy](https://github.com/dnSpyEx/dnSpy) and open the game dll (usually at `"C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll"`) and the mod dll you want to debug (usually in `"C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\ModName\.....\ModName.dll"`. 

You can also use Visual Studio but you won't be able to set breakpoints in RimWorld code. 

Finally, attach to the Unity debugger that runs inside RimWorld (use localhost or 127.0.0.1 and the port you configured in the .ini file) and you should be able to set breakpoints in RimWorld code and in your mod code too.
