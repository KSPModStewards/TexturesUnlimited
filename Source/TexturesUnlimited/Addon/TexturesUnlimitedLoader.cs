﻿using KSPShaderTools.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace KSPShaderTools
{

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class TexturesUnlimitedLoader : MonoBehaviour
    {

        /*  Custom Shader Loading for KSP
         *  Includes loading of platform-specific bundles, or 'universal' bundles.  
         *  Bundles to be loaded are determined by config files (KSP_SHADER_BUNDLE)
         *  Each bundle can have multiple shaders in it.
         *  
         *  Shader / Icon shaders are determined by another config node (KSP_SHADER_DATA)
         *  with a key for shader = <shaderName> and iconShader = <iconShaderName>
         *  
         *  Shaders are applied to models in the database through a third config node (KSP_MODEL_SHADER)
         *  --these specify which database-model-URL to apply a specific texture set to (KSP_TEXTURE_SET)
         *  
         *  Texture sets (KSP_TEXTURE_SET) can be referenced in the texture-switch module for run-time texture switching capability.
         *  
         *  
         *  //eve shader loading data -- need to examine what graphics APIs the SSTU shaders are set to build for -- should be able to build 'universal' bundles (done)
         *  https://github.com/WazWaz/EnvironmentalVisualEnhancements/blob/master/Assets/Editor/BuildABs.cs
         */

        #region REGION - Maps of shaders, texture sets, procedural textures

        public static Dictionary<string, Shader> loadedShaders = new Dictionary<string, Shader>();

        /// <summary>
        /// List of loaded shaders and corresponding icon shader.  Loaded from KSP_SHADER_DATA config nodes.
        /// </summary>
        public static Dictionary<string, IconShaderData> iconShaders = new Dictionary<string, IconShaderData>();

        /// <summary>
        /// List of loaded global texture sets.  Loaded from KSP_TEXTURE_SET config nodes.
        /// </summary>
        public static Dictionary<string, TextureSet> loadedTextureSets = new Dictionary<string, TextureSet>();


        public static Dictionary<string, TextureSet> loadedModelShaderSets = new Dictionary<string, TextureSet>();

        /// <summary>
        /// List of procedurally created 'solid color' textures to use for filling in empty texture slots in materials.
        /// </summary>
        public static Dictionary<string, Texture2D> textureColors = new Dictionary<string, Texture2D>();

        /// <summary>
        /// List of shaders with transparency, and the keywords that enable it.  Used to properly set the render-queue for materials.
        /// </summary>
        public static Dictionary<string, TransparentShaderData> transparentShaderData = new Dictionary<string, TransparentShaderData>();

        public static int diffuseTextureRenderQueue = 2000;

        public static int transparentTextureRenderQueue = 3000;

        #endregion ENDREGION - Maps of shaders, texture sets, procedural textures

        #region REGION - Config Values loaded from disk

        public static bool logAll = false;
        public static bool logReplacements = false;
        public static bool logErrors = false;

        public static int recolorGUIWidth = 400;
        public static int recolorGUISectionHeight = 540;
        public static int recolorGUITotalHeight = 100;

        public static bool alternateRender = false;

        public static ConfigNode configurationNode;

        #endregion ENDREGION - Config Values loaded from disk

        public static TexturesUnlimitedLoader INSTANCE;

        private static List<Action> postLoadCallbacks = new List<Action>();

        private static EventVoid.OnEvent partListLoadedEvent;

        private static GraphicsAPIGUI apiCheckGUI;

        public void Start()
        {
            Log.log("TexturesUnlimitedLoader - Start()");
            INSTANCE = this;
            DontDestroyOnLoad(this);
            if (partListLoadedEvent == null)
            {
                partListLoadedEvent = new EventVoid.OnEvent(onPartListLoaded);
                GameEvents.OnPartLoaderLoaded.Add(partListLoadedEvent);
            }

        }

        public void OnDestroy()
        {
            GameEvents.OnPartLoaderLoaded.Remove(partListLoadedEvent);
        }

        public void ModuleManagerPostLoad()
        {
            load();
        }

        internal void removeAPICheckGUI()
        {
            if (apiCheckGUI != null)
            {
                Component.Destroy(apiCheckGUI);
            }
        }

        private void load()
        {
            //clear any existing data in case of module-manager reload
            loadedShaders.Clear();
            loadedModelShaderSets.Clear();
            iconShaders.Clear();
            loadedTextureSets.Clear();
            textureColors.Clear();
            transparentShaderData.Clear();
            Log.log("TexturesUnlimited - Initializing shader and texture set data.");
            ConfigNode[] allTUNodes = GameDatabase.Instance.GetConfigNodes("TEXTURES_UNLIMITED");
            ConfigNode config = Array.Find(allTUNodes, m => m.GetStringValue("name") == "default");
            configurationNode = config;

            logAll = config.GetBoolValue("logAll", logAll);
            logReplacements = config.GetBoolValue("logReplacements", logReplacements);
            logErrors = config.GetBoolValue("logErrors", logErrors);
            recolorGUIWidth = config.GetIntValue("recolorGUIWidth");
            recolorGUITotalHeight = config.GetIntValue("recolorGUITotalHeight");
            recolorGUISectionHeight = config.GetIntValue("recolorGUISectionHeight");
            if (config.GetBoolValue("displayDX9Warning", true))
            {
                //disable API check as long as using stock reflection system
                //doAPICheck();
            }
            loadBundles();
            buildShaderSets();
            PresetColor.loadColors();
            loadTextureSets();
            applyToModelDatabase();
            Log.log("TexturesUnlimited - Calling PostLoad handlers");
            foreach (Action act in postLoadCallbacks) { act.Invoke(); }
            dumpUVMaps();
            fixStockBumpMaps();
            //NormMaskCreation.processBatch();
        }

        private void doAPICheck()
        {
            //check the graphics API, popup warning if using unsupported gfx (dx9/11/12/legacy-openGL)
            UnityEngine.Rendering.GraphicsDeviceType graphicsAPI = SystemInfo.graphicsDeviceType;
            if (graphicsAPI == UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore)
            {
                //noop, everything is fine
            }
            else if (graphicsAPI == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11)
            {
                //works, but needs alternate render
                alternateRender = true;
            }
            else if (graphicsAPI == UnityEngine.Rendering.GraphicsDeviceType.Direct3D9)
            {
                //has issues -- display warning, and needs alternate render
                alternateRender = true;
                if (apiCheckGUI == null)
                {
                    apiCheckGUI = this.gameObject.AddComponent<GraphicsAPIGUI>();
                    apiCheckGUI.openGUI();
                }
            }
            else
            {
                //unknown API -- display warning
                if (apiCheckGUI == null)
                {
                    apiCheckGUI = this.gameObject.AddComponent<GraphicsAPIGUI>();
                    apiCheckGUI.openGUI();
                }
            }
        }

        private void onPartListLoaded()
        {
            Log.log("TexturesUnlimited - Updating Part Icon shaders.");
            applyToPartIcons();
        }

        private static void loadBundles()
        {
            loadedShaders.Clear();
            ConfigNode[] shaderNodes = GameDatabase.Instance.GetConfigNodes("KSP_SHADER_BUNDLE");
            int len = shaderNodes.Length;
            for (int i = 0; i < len; i++)
            {
                loadBundle(shaderNodes[i], loadedShaders);
            }
        }

        private static void loadBundle(ConfigNode node, Dictionary<String, Shader> shaderDict)
        {
            string assetBundleName = "";
            if (node.HasValue("universal")) { assetBundleName = node.GetStringValue("universal"); }
            else if (Application.platform == RuntimePlatform.WindowsPlayer) { assetBundleName = node.GetStringValue("windows"); }
            else if (Application.platform == RuntimePlatform.LinuxPlayer) { assetBundleName = node.GetStringValue("linux"); }
            else if (Application.platform == RuntimePlatform.OSXPlayer) { assetBundleName = node.GetStringValue("osx"); }
            assetBundleName = KSPUtil.ApplicationRootPath + "GameData/" + assetBundleName;

            Log.log("TexturesUnlimited - Loading Shader Pack: " + node.GetStringValue("name") + " :: " + assetBundleName);

            // KSP-PartTools built AssetBunldes are in the Web format, 
            // and must be loaded using a WWW reference; you cannot use the
            // AssetBundle.CreateFromFile/LoadFromFile methods unless you 
            // manually compiled your bundles for stand-alone use
            WWW www = CreateWWW(assetBundleName);

            if (!string.IsNullOrEmpty(www.error))
            {
                Log.exception("TexturesUnlimited - Error while loading shader AssetBundle: " + www.error);
                return;
            }
            else if (www.assetBundle == null)
            {
                Log.exception("TexturesUnlimited - Could not load AssetBundle from WWW - " + www);
                return;
            }

            AssetBundle bundle = www.assetBundle;

            string[] assetNames = bundle.GetAllAssetNames();
            int len = assetNames.Length;
            Shader shader;
            for (int i = 0; i < len; i++)
            {
                if (assetNames[i].EndsWith(".shader"))
                {
                    shader = bundle.LoadAsset<Shader>(assetNames[i]);
                    Log.log("TexturesUnlimited - Loaded Shader: " + shader.name + " :: " + assetNames[i]+" from pack: "+ node.GetStringValue("name"));
                    if (shader == null || string.IsNullOrEmpty(shader.name))
                    {
                        Log.exception("ERROR: Shader did not load properly for asset name: " + assetNames[i]);
                    }
                    else if (shaderDict.ContainsKey(shader.name))
                    {
                        Log.exception("ERROR: Duplicate shader detected: " + shader.name);
                    }
                    else
                    {
                        shaderDict.Add(shader.name, shader);
                    }
                    GameDatabase.Instance.databaseShaders.AddUnique(shader);
                }
            }
            //this unloads the compressed assets inside the bundle, but leaves any instantiated shaders in-place
            bundle.Unload(false);
        }

        public static void addPostLoadCallback(Action func)
        {
            postLoadCallbacks.AddUnique(func);
        }

        public static void removePostLoadCallback(Action func)
        {
            postLoadCallbacks.Remove(func);
        }

        private static void buildShaderSets()
        {
            ConfigNode[] shaderNodes = GameDatabase.Instance.GetConfigNodes("KSP_SHADER_DATA");
            ConfigNode node;
            int len = shaderNodes.Length;
            string sName, iName;
            for (int i = 0; i < len; i++)
            {
                node = shaderNodes[i];
                sName = node.GetStringValue("shader", "KSP/Diffuse");
                iName = node.GetStringValue("iconShader", "KSP/ScreenSpaceMask");
                Log.log("Loading shader icon replacement data for: " + sName + " :: " + iName);
                Shader shader = getShader(sName);
                if (shader == null)
                {
                    Log.exception("ERROR: Could not locate base Shader for name: " + sName + " while setting up icon shaders.");
                    continue;
                }
                Shader iconShader = getShader(iName);
                if (iconShader == null)
                {
                    Log.exception("ERROR: Could not locate icon Shader for name: " + iName + " while setting up icon shaders.");
                    continue;
                }
                IconShaderData data = new IconShaderData(shader, iconShader);
                iconShaders.Add(shader.name, data);
            }
        }

        private static void loadTransparencyData()
        {
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("TRANSPARENT_SHADER");
            TransparentShaderData tsd;
            int len = nodes.Length;
            for (int i = 0; i < len; i++)
            {
                tsd = new TransparentShaderData(nodes[i]);
                transparentShaderData.Add(tsd.shader.name, tsd);
            }
        }

        /// <summary>
        /// Asset bundle loader helper method.  Creates a Unity WWW URL reference for the input file-path
        /// </summary>
        /// <param name="bundlePath"></param>
        /// <returns></returns>
        private static WWW CreateWWW(string bundlePath)
        {
            try
            {
                string name = Application.platform == RuntimePlatform.WindowsPlayer ? "file:///" + bundlePath : "file://" + bundlePath;
                return new WWW(Uri.EscapeUriString(name));
            }
            catch (Exception e)
            {
                Log.exception("Error while creating AssetBundle request: " + e);
                return null;
            }
        }

        private static void loadTextureSets()
        {
            loadedTextureSets.Clear();
            ConfigNode[] setNodes = GameDatabase.Instance.GetConfigNodes("KSP_TEXTURE_SET");
            TextureSet[] sets = TextureSet.parse(setNodes, "create");
            int len = sets.Length;
            for (int i = 0; i < len; i++)
            {
                if (loadedTextureSets.ContainsKey(sets[i].name))
                {
                    Log.exception("ERROR: Duplicate texture set definition found for name: " + sets[i].name +
                        "  This is a major configuration error that should be corrected.  Correct operation cannot be ensured.");
                }
                else
                {
                    loadedTextureSets.Add(sets[i].name, sets[i]);
                }
            }
        }

        /// <summary>
        /// Applies any 'KSP_MODEL_SHADER' definitions to models in the GameDatabase.loadedModels list.
        /// </summary>
        private static void applyToModelDatabase()
        {
            ConfigNode[] modelShaderNodes = GameDatabase.Instance.GetConfigNodes("KSP_MODEL_SHADER");
            TextureSet set = null;
            ConfigNode textureNode;
            string setName="";
            int len = modelShaderNodes.Length;
            string[] modelNames;
            GameObject model;
            for (int i = 0; i < len; i++)
            {
                textureNode = modelShaderNodes[i];
                if (textureNode.HasNode("MATERIAL"))
                {
                    set = new TextureSet(textureNode, "update");
                    setName = set.name;
                }
                else if (textureNode.HasNode("TEXTURE"))//legacy style definitions
                {
                    set = new TextureSet(textureNode, "update");
                    setName = set.name;
                }
                else if (textureNode.HasValue("textureSet"))
                {
                    setName = textureNode.GetStringValue("textureSet");
                    set = getTextureSet(setName);
                    if (set == null)
                    {
                        Log.exception("ERROR: Did not locate texture set from global cache for input name: " + setName+" while applying KSP_MODEL_SHADER with name of: "+modelShaderNodes[i].GetStringValue("name","UNKNOWN"));
                        continue;
                    }
                }
                if (!string.IsNullOrEmpty(setName) && !loadedModelShaderSets.ContainsKey(setName))
                {
                    loadedModelShaderSets.Add(setName, set);
                }
                modelNames = textureNode.GetStringValues("model");
                int len2 = modelNames.Length;
                for (int k = 0; k < len2; k++)
                {
                    model = GameDatabase.Instance.GetModelPrefab(modelNames[k]);
                    if (model != null)
                    {
                        Log.replacement("TexturesUnlimited -- Replacing textures on database model: " + modelNames[k]);
                        set.enable(model.transform, set.maskColors);
                    }
                    else
                    {
                        Log.exception("ERROR: Could not locate model: " + modelNames[k] + " while applying KSP_MODEL_SHADER with name of: " + modelShaderNodes[i].GetStringValue("name", "UNKNOWN"));
                    }
                }
            }
        }

        /// <summary>
        /// Update the part-icons for any parts using shaders found in the part-icon-updating shader map.
        /// Adjusts models specifically based on what shader they are currently using, with the goal of replacing the stock default icon shader with something more suitable.
        /// </summary>
        private static void applyToPartIcons()
        {
            //brute-force method for fixing part icon shaders
            //  iterate through entire loaded parts list
            //      iterate through every transform with a renderer component
            //          if renderer uses a shader in the shader-data-list
            //              replace shader on icon with the 'icon shader' corresponding to the current shader
            Shader iconShader;
            foreach (AvailablePart p in PartLoader.LoadedPartsList)
            {
                if (p.iconPrefab == null)//should never happen
                {
                    Log.exception("ERROR: Part: " + p.name + " had a null icon!");
                    continue;
                }
                if (p.partPrefab == null)
                {
                    Log.exception("ERROR: Part: " + p.name + " had a null prefab!");
                    continue;
                }
                bool outputName = false;//only log the adjustment a single time
                Transform pt = p.partPrefab.gameObject.transform;
                Renderer[] ptrs = pt.GetComponentsInChildren<Renderer>();
                foreach (Renderer partRenderer in ptrs)
                {
                    Material originalMeshMaterial = partRenderer.sharedMaterial;
                    if (originalMeshMaterial == null || partRenderer.sharedMaterial.shader == null)
                    {
                        if (originalMeshMaterial == null) { Log.exception("ERROR: Null material found on renderer: " + partRenderer.gameObject.name); }
                        else if (originalMeshMaterial.shader == null) { Log.exception("ERROR: Null shader found on renderer: " + partRenderer.gameObject.name); }
                        continue;
                    }
                    //part transform shader name
                    string materialShaderName = originalMeshMaterial.shader.name;
                    if (!string.IsNullOrEmpty(materialShaderName) && iconShaders.ContainsKey(materialShaderName))//is a shader that we care about
                    {
                        iconShader = iconShaders[materialShaderName].iconShader;
                        if (!outputName)
                        {
                            Log.replacement("KSPShaderLoader - Adjusting icon shaders for part: " + p.name + " for original shader:" + materialShaderName + " replacement: " + iconShader.name);
                            outputName = true;
                        }
                        //transforms in the icon prefab
                        //adjust the materials on these to use the specified shader from config
                        Transform[] iconPrefabTransforms = p.iconPrefab.gameObject.transform.FindChildren(partRenderer.name);//find transforms from icon with same name
                        foreach (Transform ictr in iconPrefabTransforms)
                        {
                            Renderer itr = ictr.GetComponent<Renderer>();
                            if (itr == null) { continue; }
                            Material mat2 = itr.material;//use .material to force non-shared material instances
                            if (mat2 == null) { continue; }
                            Log.replacement("BASE:\n" + Debug.getMaterialPropertiesDebug(originalMeshMaterial));
                            Log.replacement("PRE :\n" + Debug.getMaterialPropertiesDebug(mat2));
                            //can't just swap shaders, does some weird stuff with properties??
                            mat2.shader = iconShader;
                            //mat2.CopyPropertiesFromMaterial(originalMeshMaterial);
                            //mat2.CopyKeywordsFrom(originalMeshMaterial);
                            itr.material = mat2;//probably un-needed, but whatever
                            if (originalMeshMaterial.HasProperty("_Shininess") && mat2.HasProperty("_Smoothness"))
                            {
                                mat2.SetFloat("_Smoothness", originalMeshMaterial.GetFloat("_Shininess"));
                            }
                            Log.replacement("POST:\n" + Debug.getMaterialPropertiesDebug(mat2));
                            //TODO -- since these parts have already been mangled and had the stock icon shader applied
                            //  do any properties not present on stock parts need to be re-seated, or do they stay resident in
                            //  the material even if the current shader lacks the property?
                            //TODO -- check the above, esp. in regards to keywords now that TU is using them
                            //  need to make sure keywords stay resident in the material itself between shader swaps.
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Utility method to dump UV maps from every model currently in the model database.
        /// TODO -- has issues/errors on some models/meshes/renderers (might be a skinned-mesh-renderer problem...)
        /// TODO -- has issues with part names that have invalid characters for file-system use -- should sanitize the names
        /// </summary>
        public static void dumpUVMaps(bool force = false)
        {
            UVMapExporter exporter = new UVMapExporter();
            ConfigNode node = TexturesUnlimitedLoader.configurationNode.GetNode("UV_EXPORT");
            bool export = node.GetBoolValue("exportUVs", false);
            if (!export && !force) { return; }
            string path = node.GetStringValue("exportPath", "exportedUVs");
            exporter.width = node.GetIntValue("width", 1024);
            exporter.height = node.GetIntValue("height", 1024);
            exporter.stroke = node.GetIntValue("thickness", 1);
            foreach (GameObject go in GameDatabase.Instance.databaseModel)
            {
                exporter.exportModel(go, path);
            }
        }

        /// <summary>
        /// Runs through a list of configs and fixes any stock Normal Map textures that are incorrectly formatted for use with the Unity Standard shader.
        /// Each texture to be corrected must be specified separately in a config file.
        /// </summary>
        public static void fixStockBumpMaps()
        {
            long elapsedTime = 0;
            ConfigNode[] rootNodes = GameDatabase.Instance.GetConfigNodes("STOCK_NORMAL_CORRECTION");
            if (rootNodes == null || rootNodes.Length <= 0)
            {
                Log.debug("Stock normal correction nodes were null! - Nothing to correct.");
                return;
            }
            Stopwatch sw = new Stopwatch();
            foreach (ConfigNode rootNode in rootNodes)
            {
                ConfigNode[] texNodes = rootNode.GetNodes("TEXTURE");
                foreach (ConfigNode texNode in texNodes)
                {
                    sw.Start();
                    string texName = texNode.GetStringValue("name");
                    string xSource = texNode.GetStringValue("xSourceChannel");
                    string ySource = texNode.GetStringValue("ySourceChannel");

                    Log.debug("TexturesUnlimited - Correcting Stock Normal Map: " + texName + " xSource: " + xSource + " ySource: " + ySource);

                    GameDatabase.TextureInfo info = GameDatabase.Instance.GetTextureInfo(texName);
                    if (info == null)
                    {
                        Log.debug("ERROR: Source texture was null for path: " + texName);
                        continue;
                    }
                    Texture2D sourceTexture = info.texture;
                    if (sourceTexture == null)
                    {
                        Log.debug("ERROR: Source texture was null for path: " + texName);
                        continue;
                    }

                    if (!sourceTexture.isReadable)
                    {
                        Log.debug("Source Texture is unreadable, blitting through RenderTexture to make readable.");
                        Log.debug("Source Texture format: " + sourceTexture.format);
                        RenderTexture blitTarget = RenderTexture.GetTemporary(sourceTexture.width, sourceTexture.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
                        Graphics.Blit(sourceTexture, blitTarget);
                        RenderTexture prev = RenderTexture.active;
                        RenderTexture.active = blitTarget;
                        Texture2D newSource = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.ARGB32, false);
                        newSource.ReadPixels(new Rect(0, 0, sourceTexture.width, sourceTexture.height), 0, 0);
                        newSource.Apply(true, false);
                        sourceTexture = newSource;
                        RenderTexture.active = prev;
                        RenderTexture.ReleaseTemporary(blitTarget);
                    }

                    Texture2D temp = new Texture2D(sourceTexture.width, sourceTexture.height, sourceTexture.format, sourceTexture.mipmapCount > 0);
                    Color[] sourcePixels = sourceTexture.GetPixels(0, 0, sourceTexture.width, sourceTexture.height);
                    int len = sourcePixels.Length;
                    Color c, c1;
                    float r, g, b, a;
                    for (int i = 0; i < len; i++)
                    {
                        c = sourcePixels[i];//source pixel
                        c1 = c;//copy of source pixel
                        r = c.r;
                        g = c.g;
                        b = c.b;
                        a = c.a;

                        c.r = 1;
                        c.g = getChannelColor(c1, ySource);
                        c.b = 1;
                        c.a = getChannelColor(c1, xSource);
                        sourcePixels[i] = c;
                    }
                    temp.SetPixels(0, 0, sourceTexture.width, sourceTexture.height, sourcePixels);
                    temp.Apply(true, true);
                    info.texture = temp;
                    sw.Stop();
                    elapsedTime += sw.ElapsedMilliseconds;
                    Log.debug("Texture update time: " + sw.ElapsedMilliseconds + " ms.");
                    sw.Reset();
                }
            }
            Log.debug("Total texture correction time: " + elapsedTime + " ms.");
        }

        private static float getChannelColor(Color color, string channel)
        {
            if (channel == "r") { return color.r; }
            if (channel == "g") { return color.g; }
            if (channel == "b") { return color.b; }
            if (channel == "a") { return color.a; }
            Log.debug("Unrecognized channel specified: " + channel + ".  This must be either 'r', 'g', 'b', or 'a'.  Using value from 'a' channel as default.");
            return color.a;
        }

        /// <summary>
        /// Return a shader by name.  First checks the TU shader dictionary, then checks the GameDatabase.databaseShaders list, and finally falls-back to standard Unity Shader.Find() method.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Shader getShader(string name)
        {
            if (loadedShaders.ContainsKey(name))
            {
                return loadedShaders[name];
            }
            Shader s = GameDatabase.Instance.databaseShaders.Find(m => m.name == name);
            if (s != null)
            {
                return s;
            }
            return Shader.Find(name);
        }

        /// <summary>
        /// Find a global texture set from model shader set cache with a name that matches the input name.  Returns null if not found.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static TextureSet getModelShaderTextureSet(string name)
        {
            TextureSet s = null;
            if (loadedModelShaderSets.TryGetValue(name, out s))
            {
                return s;
            }
            Log.exception("ERROR: Could not locate TextureSet for MODEL_SHADER from global cache for the input name of: " + name);
            return null;
        }

        /// <summary>
        /// Find a global texture set from database with a name that matches the input name.  Returns null if not found.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static TextureSet getTextureSet(string name)
        {
            TextureSet s = null;
            if (loadedTextureSets.TryGetValue(name, out s))
            {
                return s;
            }
            Log.exception("ERROR: Could not locate TextureSet from global cache for the input name of: " + name);
            return null;
        }

        /// <summary>
        /// Return an array of texture sets for the 'name' values from within the input config node array.  Returns an empty array if none are found.
        /// </summary>
        /// <param name="setNodes"></param>
        /// <returns></returns>
        public static TextureSet[] getTextureSets(ConfigNode[] setNodes)
        {
            int len = setNodes.Length;
            TextureSet[] sets = new TextureSet[len];
            for (int i = 0; i < len; i++)
            {
                sets[i] = getTextureSet(setNodes[i].GetStringValue("name"));
            }
            return sets;
        }

        /// <summary>
        /// Return an array of texture sets for the values from within the input string array.  Returns an empty array if none are found.
        /// </summary>
        /// <param name="setNodes"></param>
        /// <returns></returns>
        public static TextureSet[] getTextureSets(string[] setNames)
        {
            int len = setNames.Length;
            TextureSet[] sets = new TextureSet[len];
            for (int i = 0; i < len; i++)
            {
                sets[i] = getTextureSet(setNames[i]);
            }
            return sets;
        }

        /// <summary>
        /// Input should be a string with R,G,B,A values specified in comma-separated byte notation
        /// </summary>
        /// <param name="stringColor"></param>
        /// <returns></returns>
        public static Texture2D getTextureColor(string stringColor)
        {
            string rgbaString;
            Color c = Utils.parseColor(stringColor);
            //just smash the entire thing together to create a unique key for the color
            rgbaString = "" + c.r +":"+ c.g + ":" + c.b + ":" + c.a;
            Texture2D tex = null;
            if (textureColors.TryGetValue(rgbaString, out tex))
            {
                return tex;
            }
            else
            {
                int len = 64 * 64;
                Color[] pixelData = new Color[len];
                for (int i = 0; i < len; i++)
                {
                    pixelData[i] = c;
                }
                tex = new Texture2D(64, 64, TextureFormat.ARGB32, false);
                tex.name = "TUTextureColor:" + rgbaString;
                tex.SetPixels(pixelData);
                tex.Apply(false, true);
                textureColors.Add(rgbaString, tex);
                return tex;
            }
        }

        /// <summary>
        /// Return true/false if the input material uses a shader that supports transparency
        /// AND transparency is currently enabled on the material from keywords (if applicable).
        /// </summary>
        /// <param name="mat"></param>
        /// <returns></returns>
        public static bool isTransparentMaterial(Material mat)
        {
            return isTransparentShader(mat.shader.name);
        }

        public static bool isTransparentShader(string name)
        {
            TransparentShaderData tsd = null;
            if (transparentShaderData.TryGetValue(name, out tsd))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

    }

    public class TransparentShaderData
    {
        public readonly Shader shader;
        public readonly string shaderName;

        public TransparentShaderData(ConfigNode node)
        {
            shaderName = node.GetStringValue("name");
            shader = TexturesUnlimitedLoader.getShader(shaderName);
        }
    }

    /// <summary>
    /// Shader to IconShader map <para/>
    /// Used to fix incorrect icon shaders when recoloring shaders are used.
    /// </summary>
    public class IconShaderData
    {
        public readonly Shader shader;
        public readonly Shader iconShader;

        public IconShaderData(Shader shader, Shader iconShader)
        {
            this.shader = shader;
            this.iconShader = iconShader;
        }
    }
    
    public struct RecoloringDataPreset
    {
        public string name;
        public string title;
        public Color color;
        public float specular;
        public float metallic;
        public float detail;

        public RecoloringDataPreset(ConfigNode node)
        {
            name = node.GetStringValue("name");
            title = node.GetStringValue("title");
            color = node.GetColor("color");
            specular = node.GetColorChannelValue("specular");
            metallic = node.GetColorChannelValue("metallic");
            detail = node.GetColorChannelValue("detail");
        }

        public RecoloringData getRecoloringData()
        {
            return new RecoloringData(color, specular, metallic, detail);
        }
    }

    /// <summary>
    /// Defines a group of presets colors to be made available in the recoloring GUI.  These will be defined by root-level configuration nodes, in the form of:
    /// PRESET_COLOR_GROUP
    /// {
    ///     name = unique_name_here
    ///     color = name_of_preset_color
    ///     color = name_of_another_preset_color
    /// }
    /// </summary>
    public class RecoloringDataPresetGroup
    {
        public string name;
        public List<RecoloringDataPreset> colors = new List<RecoloringDataPreset>();
        public RecoloringDataPresetGroup(string name) { this.name = name; }
    }

    public class PresetColor
    {

        private static List<RecoloringDataPreset> colorList = new List<RecoloringDataPreset>();
        private static Dictionary<String, RecoloringDataPreset> presetColors = new Dictionary<string, RecoloringDataPreset>();
        private static List<RecoloringDataPresetGroup> presetGroupList = new List<RecoloringDataPresetGroup>();
        private static Dictionary<string, RecoloringDataPresetGroup> presetGroups = new Dictionary<string, RecoloringDataPresetGroup>();
        
        
        internal static void loadColors()
        {
            colorList.Clear();
            presetColors.Clear();
            ConfigNode[] colorNodes = GameDatabase.Instance.GetConfigNodes("KSP_COLOR_PRESET");
            int len = colorNodes.Length;
            for (int i = 0; i < len; i++)
            {
                RecoloringDataPreset data = new RecoloringDataPreset(colorNodes[i]);
                if (!presetColors.ContainsKey(data.name))
                {
                    presetColors.Add(data.name, data);
                    colorList.Add(data);
                    loadPresetIntoGroup(data, "FULL");
                }
            }
            ConfigNode[] groupNodes = GameDatabase.Instance.GetConfigNodes("PRESET_COLOR_GROUP");
            len = groupNodes.Length;
            for (int i = 0; i < len; i++)
            {
                string name = groupNodes[i].GetStringValue("name");
                string[] colorNames = groupNodes[i].GetStringValues("color");
                for (int k = 0; k < colorNames.Length; k++)
                {
                    RecoloringDataPreset data;
                    if (presetColors.TryGetValue(colorNames[k], out data))
                    {
                        loadPresetIntoGroup(data, name);
                    }
                }
            }
        }

        internal static void loadPresetIntoGroup(RecoloringDataPreset preset, string group)
        {
            RecoloringDataPresetGroup colors;
            if (!presetGroups.TryGetValue(group, out colors))
            {
                colors = new RecoloringDataPresetGroup(group);
                presetGroups.Add(group, colors);
                presetGroupList.Add(colors);
            }
            if (!colors.colors.Contains(preset))
            {
                colors.colors.Add(preset);
            }
        }

        public static RecoloringDataPreset getColor(string name)
        {
            if (!presetColors.ContainsKey(name))
            {
                MonoBehaviour.print("ERROR: No Color data for name: " + name + " returning the first available color preset.");
                if (colorList.Count > 0)
                {
                    return colorList[0];
                }
                MonoBehaviour.print("ERROR: No preset colors defined, could not return a valid preset.");
                return new RecoloringDataPreset()
                {
                    color = Color.gray,
                    metallic = 0,
                    specular = 0,
                    name = "ERROR",
                    title = "ERROR",
                };
            }
            return presetColors[name];
        }

        public static List<RecoloringDataPreset> getColorList() { return colorList; }

        public static List<RecoloringDataPreset> getColorList(string group)
        {
            RecoloringDataPresetGroup g;
            if (!presetGroups.TryGetValue(group, out g))
            {
                Log.error("No preset group found for name: " + group);
                return colorList;
            }            
            return g.colors;
        }

        public static List<RecoloringDataPresetGroup> getGroupList()
        {
            return presetGroupList;
        }

    }

}
