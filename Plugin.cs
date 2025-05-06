using System.Collections.Generic;
using System.IO;
using BepInEx;
using REPOLib.Modules;
using UnityEngine;

namespace Minimotes;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency(REPOLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
internal class Plugin : BaseUnityPlugin
{
    private void Awake()
    {
        string pluginFolderPath = Path.GetDirectoryName(Info.Location);
        string assetBundleFilePath = Path.Combine(pluginFolderPath, "minimotes");

        AssetBundle assetBundle = AssetBundle.LoadFromFile(assetBundleFilePath);

        if (assetBundle == null )
        {
            Logger.LogError("Failed to load Hiccubz assetbundle");
            return;
        }

        List<string> list = ["Valuables - Generic"];

        GameObject Piccubz = assetBundle.LoadAsset<GameObject>("Valuable Hiccubz");
        Valuables.RegisterValuable(Piccubz, list);
    }
}
