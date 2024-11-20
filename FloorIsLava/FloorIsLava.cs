using BepInEx;
using BepInEx.Configuration;
using RoR2;
using RiskOfOptions;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;
using UnityEngine;
using RoR2.Orbs;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using RoR2.Networking;
using System.Collections.Generic;
using static RoR2.CharacterBody;
using UnityEngine.Networking;
using System.Linq;

namespace FloorIsLava
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class FloorIsLava : BaseUnityPlugin
    {

        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "TaranDev";
        public const string PluginName = "FloorIsLava";
        public const string PluginVersion = "1.0.0";

        public static ConfigEntry<bool> takeDamageOnAllGround;

        public static ConfigEntry<float> healthDamagePercentage;

        public static ConfigEntry<float> lavaDurationDamageScaling;

        public static ConfigEntry<float> lavaCooldown;

        public static ConfigEntry<bool> lavaDamageEnemies;

        public static ConfigEntry<float> lavaEnemyDamage;

        public static ConfigEntry<bool> lavaCanKillPlayers;

        public static ConfigEntry<bool> lavaCanKillEnemies;

        public static ConfigEntry<bool> usePennies;

        public static ConfigEntry<float> penniesPayMultiplier;

        public static ConfigEntry<bool> easySkyMeadows;

        public static ConfigEntry<float> lavaBrightness;

        private static Material lavaMaterial = Addressables.LoadAssetAsync<Material>((object)"RoR2/DLC2/helminthroost/Assets/matHRLava.mat").WaitForCompletion();
        private static Material SMMaterial = Addressables.LoadAssetAsync<Material>((object)"RoR2/Base/skymeadow/matSMTerrain.mat").WaitForCompletion();
        private static SurfaceDef lavaDef = Addressables.LoadAssetAsync<SurfaceDef>((object)"RoR2/Base/Common/sdLava.asset").WaitForCompletion();
        private static SurfaceDef dirtDef = Addressables.LoadAssetAsync<SurfaceDef>((object)"RoR2/Base/Common/sdSkymeadowDirt.asset").WaitForCompletion();
        private static readonly Mesh lavaMesh = Addressables.LoadAssetAsync<Mesh>((object)"RoR2/DLC2/helminthroost/Assets/mdlHRLava.fbx").WaitForCompletion();
        private static readonly Shader lavaShader = Addressables.LoadAssetAsync<Shader>((object)"RoR2/Base/Shaders/HGStandard.shader").WaitForCompletion();
        
        public void Awake()
        {
            Log.Init(Logger);
            configs();
        }

        IDictionary<CharacterBody, float> inLavaDuration = new Dictionary<CharacterBody, float>();

        private void OnEnable()
        {
            On.RoR2.SceneDirector.Start += SceneDirector_Start;
            On.RoR2.VoidRaidGauntletController.RpcActivateDonut += VoidStageNewDonut;
            On.RoR2.CharacterBody.HandleLavaDamage += HandleLavaDamage;
            On.RoR2.CharacterBody.InflictLavaDamage += InflictLavaDamage;
            On.RoR2.CharacterBody.SetInLava += SetInLava;
        }

        private void SetInLava(On.RoR2.CharacterBody.orig_SetInLava orig, CharacterBody self, bool b)
        {
            b = b || takeDamageOnAllGround.Value && self.characterMotor.isGrounded;
            if (self.inLava != b)
            {
                if (!NetworkServer.active && self.hasAuthority)
                {
                    self.CallCmdSetInLava(b);
                }
                self.inLava = b;
            }
        }

        private void InflictLavaDamage(On.RoR2.CharacterBody.orig_InflictLavaDamage orig, CharacterBody self)
        {
            DamageInfo damageInfo = new DamageInfo();
            damageInfo.damage = 0f;
            
            if (self.isChampion || self.isBoss || self.teamComponent.teamIndex == TeamIndex.Monster || self.teamComponent.teamIndex == TeamIndex.Void)
            {
                if (lavaDamageEnemies.Value)
                {
                    damageInfo.damage = self.healthComponent.fullCombinedHealth * (lavaEnemyDamage.Value / 100f) + lavaDurationDamageScaling.Value * Mathf.Floor(inLavaDuration[self]);
                    damageInfo.damageType = DamageType.IgniteOnHit;
                    damageInfo.position = self.footPosition;
                    if (damageInfo.damage > 0f)
                    {
                        if (lavaCanKillEnemies.Value || self.healthComponent.combinedHealth - damageInfo.damage >= 1)
                        {
                            self.healthComponent.TakeDamage(damageInfo);
                        }
                    }
                }
            }
            else
            {
                damageInfo.damage = self.healthComponent.fullCombinedHealth * (healthDamagePercentage.Value / 100f);

                if(lavaDurationDamageScaling.Value > 0.01)
                {
                    damageInfo.damage = damageInfo.damage + damageInfo.damage * lavaDurationDamageScaling.Value * Mathf.Floor(inLavaDuration[self]);
                }
                damageInfo.damageType = DamageType.IgniteOnHit;
                damageInfo.position = self.footPosition;
                if (damageInfo.damage > 0f)
                {
                    if (lavaCanKillPlayers.Value || self.healthComponent.combinedHealth - damageInfo.damage >= 1)
                    {
                        self.healthComponent.TakeDamage(damageInfo);
                        if (usePennies.Value)
                        {
                            if (self.healthComponent.itemCounts.goldOnHurt > 0)
                            {
                                int num11 = 3;
                                GoldOrb goldOrb2 = new GoldOrb();
                                goldOrb2.origin = damageInfo.position;
                                goldOrb2.target = self.mainHurtBox;
                                goldOrb2.goldAmount = (uint)((float)(self.healthComponent.itemCounts.goldOnHurt * num11) * Run.instance.difficultyCoefficient * penniesPayMultiplier.Value);
                                OrbManager.instance.AddOrb(goldOrb2);
                                EffectManager.SimpleImpactEffect(HealthComponent.AssetReferences.gainCoinsImpactEffectPrefab, damageInfo.position, Vector3.up, transmit: true);
                            }
                        }
                    }
                }
            }
            
        }

        private void HandleLavaDamage(On.RoR2.CharacterBody.orig_HandleLavaDamage orig, CharacterBody self, float deltaTime)
        {

            if(!inLavaDuration.ContainsKey(self))
            {
                inLavaDuration.Add(self, 0f);
            }

            bool flag = (self.bodyFlags & BodyFlags.ImmuneToLava) != 0;
            if (self.inLava && NetworkServer.active)
            {
                self.lavaTimer -= deltaTime;

                inLavaDuration[self] += deltaTime;

                if (self.lavaTimer <= 0f && !flag)
                {
                    self.InflictLavaDamage();
                    self.lavaTimer = lavaCooldown.Value;
                }
            }
            else
            {
                self.lavaTimer = 0f;
                inLavaDuration[self] = 0f;
            }
        }

        private void OnDisable()
        {
            On.RoR2.SceneDirector.Start -= SceneDirector_Start;
            On.RoR2.VoidRaidGauntletController.RpcActivateDonut -= VoidStageNewDonut;
            On.RoR2.CharacterBody.HandleLavaDamage -= HandleLavaDamage;
            On.RoR2.CharacterBody.InflictLavaDamage -= InflictLavaDamage;
            On.RoR2.CharacterBody.SetInLava -= SetInLava;
        }

        List<GameObject> lavaObjects;

        private void SceneDirector_Start(On.RoR2.SceneDirector.orig_Start orig, SceneDirector self)
        {
            
            orig(self);

            inLavaDuration = new Dictionary<CharacterBody, float>();

            UpdateSceneMaterials();
        }

        private void UpdateSceneMaterials()
        {
            lavaObjects = new List<GameObject>();

            Scene activeScene = SceneManager.GetActiveScene();
            SceneInfo instance = SceneInfo.instance;
            GameObject val;

            lavaMaterial.SetFloat("_FresnelPower", 3 - lavaBrightness.Value + 0.01f);
            lavaMaterial.SetFloat("_FresnelBoost", lavaBrightness.Value);

            switch (activeScene.name)
            {
                // Title
                case "title":
                    Title();
                    break;

                // Stage 1
                case "snowyforest":
                    SiphonedForest();
                    break;
                case "golemplains":
                case "golemplains2":
                    TitanicPlains();
                    break;
                case "blackbeach":
                    DistantRoost1();
                    break;
                case "blackbeach2":
                    DistantRoost2();
                    break;
                case "lakes":
                case "lakesnight":
                    Lakes();
                    break;
                case "village":
                case "villagenight":
                    ShatteredAbodes();
                    break;

                // Stage 2
                case "goolake":
                    AbandonedAqueduct();
                    break;
                case "foggyswamp":
                    WetlandAspect();
                    break;
                case "ancientloft":
                    AphelianSanctuary();
                    break;
                case "lemuriantemple":
                    ReformedAlter();
                    break;

                // Stage 3
                case "frozenwall":
                    RallypointDelta();
                    break;
                case "wispgraveyard":
                    ScorchedAcres();
                    break;
                case "sulfurpools":
                    SulfurPools();
                    break;
                case "habitat":
                case "habitatfall":
                    TreebornColony();
                    break;

                // Stage 4
                case "dampcavesimple":
                    AbyssalDepths();
                    break;
                case "shipgraveyard":
                    SirensCall();
                    break;
                case "rootjungle":
                    SunderedGrove();
                    break;
                case "meridian":
                    PrimeMeridian();
                    break;

                // Stage 5
                case "skymeadow":
                    SkyMeadow();
                    break;
                case "helminthroost":
                    HelminthHatchery();
                    break;

                // Moon
                case "moon2":
                    Moon2();
                    break;

                // Other
                case "mysteryspace":
                    MomentFractured();
                    break;
                case "bazaar":
                    Bazaar();
                    break;
                case "goldshores":
                    GildedCoast();
                    break;
                case "arena":
                    VoidFields();
                    break;
                case "voidstage":
                    VoidLocus();
                    break;
                case "voidraid":
                    Planetarium();
                    break;
            }

            MeshRenderer[] array = Object.FindObjectsOfType(typeof(MeshRenderer)) as MeshRenderer[];
            MeshRenderer[] array2 = array;
            foreach (MeshRenderer val2 in array2)
            {
                GameObject gameObject = ((Component)val2).gameObject;
                if ((Object)(object)gameObject != (Object)null)
                {
                    foreach (Material m in val2.materials) {
                        if ((m && (m.name == "matHRLava (Instance)" || m.name == "matHRLava" || m.name == "matHRLava (Material)" || m.name.Contains("matHRLava"))))
                        {
                            lavaObjects.Add(gameObject);
                            if (!gameObject.GetComponent<SurfaceDefProvider>())
                            {
                                gameObject.AddComponent<SurfaceDefProvider>();
                            }
                            gameObject.GetComponent<SurfaceDefProvider>().surfaceDef = lavaDef;
                            gameObject.GetComponent<SurfaceDefProvider>().surfaceDef.impactEffectPrefab = lavaDef.footstepEffectPrefab;
                            gameObject.GetComponent<SurfaceDefProvider>().surfaceDef.footstepEffectPrefab = lavaDef.footstepEffectPrefab;

                        }
                        else if (gameObject.name.Contains("Decal") || val2.material.name.Contains("Decal") || gameObject.GetComponent<MeshFilter>().mesh.name.Contains("Decal"))
                        {
                            gameObject.SetActive(false);
                        }
                    }
                    
                }
            }
        }

        // Title
        private void Title()
        {
            GameObject.Find("HOLDER: Title Background").transform.GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            GameObject.Find("HOLDER: Title Background").transform.GetChild(0).GetChild(3).gameObject.SetActive(false);

        }

        // Stage 1

        private void SiphonedForest()
        {
            // DONE

            Material lavaMaterialScaled10 = new Material(lavaMaterial);
            lavaMaterialScaled10.mainTextureScale = new Vector2(15, 15);

            Material lavaMaterialScaled50 = new Material(lavaMaterial);
            lavaMaterialScaled50.mainTextureScale = new Vector2(1, 1);

            Transform terrain = GameObject.Find("HOLDER: Terrain").transform;
            // Sap
            terrain.GetChild(5).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(6).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(7).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(8).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
            terrain.GetChild(9).GetComponent<MeshRenderer>().material = lavaMaterialScaled10;
            terrain.GetChild(10).GetComponent<MeshRenderer>().material = lavaMaterial;
        }

        private void DistantRoost1()
        {
            Transform terrain = GameObject.Find("GAMEPLAY SPACE").transform.GetChild(1).transform;
            GameObject.Find("blackbeachTerrainFull").GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;

            Transform distantTerrain = GameObject.Find("SKYBOX").transform;
            distantTerrain.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;
            for (int i = 6; i < distantTerrain.childCount; i++)
            {
                distantTerrain.GetChild(i).GetComponent<MeshRenderer>().material = lavaMaterial;
            }
        }

        private void DistantRoost2()
        {
            Transform terrain = GameObject.Find("HOLDER: Terrain").transform.GetChild(0).transform;
            terrain.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterial;

            Transform water = GameObject.Find("HOLDER: Water").transform.GetChild(0);
            water.GetComponent<MeshRenderer>().material = lavaMaterial;

            Transform distantTerrain = GameObject.Find("HOLDER: Distant Terrain").transform;
            for (int i = 0; i < distantTerrain.childCount; i++)
            {
                distantTerrain.GetChild(i).GetComponent<MeshRenderer>().material = lavaMaterial;
            }
        }

        private void TitanicPlains()
        {
            Material lavaMaterialScaled50 = new Material(lavaMaterial);
            lavaMaterialScaled50.mainTextureScale = new Vector2(0.0000000000000000000000000000000000000000000000000000000000000001f, 0.0000000000000000000000000000000000000000000000000000000000000001f);

            MeshRenderer[] array = Object.FindObjectsOfType(typeof(MeshRenderer)) as MeshRenderer[];
            MeshRenderer[] array2 = array;
            foreach (MeshRenderer val2 in array2)
            {
                GameObject gameObject = ((Component)val2).gameObject;
                if ((Object)(object)gameObject != (Object)null)
                {
                    if (((gameObject.name.Contains("Terrain") || gameObject.name == "GP_Wall North") && (Object)(object)((Renderer)val2).material))
                    {
                        ((Renderer)val2).material = lavaMaterialScaled50;
                        //gameObject.GetComponent<SurfaceDefProvider>().surfaceDef = lavaDef;
                    }
                }
            }

            Transform child2 = GameObject.Find("HOLDER: Water").transform;
            child2.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;

            if (child2.childCount > 1)
            {
                child2.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;
            }

        }

        private void Lakes()
        {
            Material lavaMaterialScaled50 = new Material(lavaMaterial);
            lavaMaterialScaled50.mainTextureScale = new Vector2(0.0000000000000000000000000000000000000000000000000000000000000001f, 0.0000000000000000000000000000000000000000000000000000000000000001f);

            Material lavaMaterialScaled2 = new Material(lavaMaterial);
            lavaMaterialScaled2.mainTextureScale = new Vector2(2, 2);

            MeshRenderer[] array = Object.FindObjectsOfType(typeof(MeshRenderer)) as MeshRenderer[];
            MeshRenderer[] array2 = array;
            foreach (MeshRenderer val2 in array2)
            {
                GameObject gameObject = ((Component)val2).gameObject;
                if ((Object)(object)gameObject != (Object)null)
                {
                    if (((gameObject.name.Contains("Terrain")) && !((gameObject.name.Contains("Tree")) || (gameObject.name.Contains("Flower")) || (gameObject.name.Contains("Ship"))) && (Object)(object)((Renderer)val2).material))
                    {
                        ((Renderer)val2).material = lavaMaterialScaled50;
                        if (gameObject.name != "TLWater" && !gameObject.GetComponent<MeshCollider>())
                        {
                            gameObject.AddComponent<MeshCollider>();
                        }
                        //gameObject.GetComponent<SurfaceDefProvider>().surfaceDef = lavaDef;
                    }
                    if (((gameObject.name.Contains("Water")) && !((gameObject.name.Contains("Tree")) || (gameObject.name.Contains("Flower")) || (gameObject.name.Contains("Ship"))) && (Object)(object)((Renderer)val2).material))
                    {
                        ((Renderer)val2).material = lavaMaterialScaled2;
                        if(gameObject.name != "TLWater" && !gameObject.GetComponent<MeshCollider>())
                        {
                            gameObject.AddComponent<MeshCollider>();
                        }
                        //gameObject.GetComponent<SurfaceDefProvider>().surfaceDef = lavaDef;
                    }
                }
            }
        }

        // Stage 2

        private void ShatteredAbodes()
        {
            // DONE

            Material lavaMaterialScaled50 = new Material(lavaMaterial);
            lavaMaterialScaled50.mainTextureScale = new Vector2(0.01f, 0.01f);

            Transform child2 = GameObject.Find("HOLDER: Art").transform;
            child2.GetChild(7).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
            child2.GetChild(8).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
            child2.GetChild(9).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
        }

        private void AbandonedAqueduct()
        {
            // DONE

            Material lavaMaterialScaled2 = new Material(lavaMaterial);
            lavaMaterialScaled2.mainTextureScale = new Vector2(2, 2);

            GameObject.Find("HOLDER: GameplaySpace").transform.GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterial;
            // Goo water pools
            Transform miscProps = GameObject.Find("HOLDER: Misc Props").transform;
            miscProps.GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterialScaled2;
            miscProps.GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterialScaled2;
        }

        private void WetlandAspect()
        {
            Material lavaMaterialScaled50 = new Material(lavaMaterial);
            lavaMaterialScaled50.mainTextureScale = new Vector2(0.00000001f, 0.00000001f);

            Material lavaMaterialScaled2 = new Material(lavaMaterial);
            lavaMaterialScaled2.mainTextureScale = new Vector2(2, 2);

            GameObject.Find("HOLDER: Skybox").transform.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            Transform environment = GameObject.Find("HOLDER: Hero Assets").transform;
            // Water
            environment.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled2;
            environment.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled2;
            Transform terrain = environment.GetChild(2).transform;
            terrain.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
            terrain.GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
            terrain.GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
            terrain.GetChild(4).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
        }

        private void AphelianSanctuary()
        {
            // DONE

            Material lavaMaterialScaled1 = new Material(lavaMaterial);
            lavaMaterialScaled1.mainTextureScale = new Vector2(1, 1);

            Transform terrain = GameObject.Find("HOLDER: Terrain").transform;
            // Terrain
            Material[] m = terrain.GetChild(1).GetComponent<MeshRenderer>().materials;
            m[1] = lavaMaterialScaled1;
            terrain.GetChild(1).GetComponent<MeshRenderer>().materials = m;
            terrain.GetChild(7).GetComponent<MeshRenderer>().materials = m;
            terrain.GetChild(8).GetComponent<MeshRenderer>().materials = m;
            terrain.GetChild(9).GetComponent<MeshRenderer>().materials = m;

            terrain.GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(4).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(5).GetComponent<MeshRenderer>().material = lavaMaterial;

            Transform water = GameObject.Find("HOLDER: Water").transform;
            // Waterfall
            water.GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            water.GetChild(0).GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;
            // Water
            water.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;

            // Water that sometimes spawnes
            Transform gatedWater = GameObject.Find("HOLDER: Gated Stuff").transform;
            if(gatedWater.GetChild(4))
            {
                gatedWater.GetChild(4).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            }
            if (gatedWater.GetChild(5))
            {
                gatedWater.GetChild(5).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            }
        }

        private void ReformedAlter()
        {
            Material lavaMaterialScaled40 = new Material(lavaMaterial);
            lavaMaterialScaled40.mainTextureScale = new Vector2(0.05f, 0.05f);

            Material lavaMaterialScaled20 = new Material(lavaMaterial);
            lavaMaterialScaled20.mainTextureScale = new Vector2(2, 2);

            Transform environment = GameObject.Find("HOLDER:Terrain").transform;
            // Water
            environment.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled20;
            environment.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled20;
            environment.GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterialScaled20;
            environment.GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterialScaled20;
            // Terrain
            Transform terrain = environment.GetChild(5).transform;
            terrain.GetChild(7).GetComponent<MeshRenderer>().material = lavaMaterialScaled40;
            terrain.GetChild(9).GetComponent<MeshRenderer>().material = lavaMaterialScaled40;
        }

        // Stage 3

        private void RallypointDelta()
        {
            GameObject.Find("HOLDER: GAMEPLAY SPACE").transform.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;

            Transform distantTerrain = GameObject.Find("HOLDER: Skybox").transform;
            distantTerrain.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            distantTerrain.GetChild(7).GetComponent<MeshRenderer>().material = lavaMaterial;
            distantTerrain.GetChild(8).GetComponent<MeshRenderer>().material = lavaMaterial;
            distantTerrain.GetChild(9).GetComponent<MeshRenderer>().material = lavaMaterial;
            distantTerrain.GetChild(10).GetComponent<MeshRenderer>().material = lavaMaterial;
            distantTerrain.GetChild(11).GetComponent<MeshRenderer>().material = lavaMaterial;
            distantTerrain.GetChild(12).GetComponent<MeshRenderer>().material = lavaMaterial;
            distantTerrain.GetChild(13).GetComponent<MeshRenderer>().material = lavaMaterial;
            distantTerrain.GetChild(4).GetComponent<MeshRenderer>().material = lavaMaterial;

            GameObject.Find("HOLDER: Artifact Formula Area").transform.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
        }

        private void ScorchedAcres()
        {
            // DONE

            Material lavaMaterialScaledTerrain = new Material(lavaMaterial);
            lavaMaterialScaledTerrain.mainTextureScale = new Vector2(1000, 1000);

            Material lavaMaterialScaled07 = new Material(lavaMaterial);
            lavaMaterialScaled07.mainTextureScale = new Vector2(0.3f, 0.3f);

            Transform distantTerrain = GameObject.Find("SKYBOX").transform;
            distantTerrain.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaledTerrain;
            distantTerrain.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaledTerrain;

            Transform entireSceneHolder = GameObject.Find("ENTIRE SCENE HOLDER").transform;

            Transform terrain = entireSceneHolder.GetChild(0).transform;
            terrain.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaledTerrain;
            terrain.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaledTerrain;
            terrain.GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterialScaledTerrain;

            Transform templePieces = entireSceneHolder.GetChild(4).transform;

            Transform forestBridge = templePieces.GetChild(0).transform;
            forestBridge.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            forestBridge.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            forestBridge.GetChild(1).GetChild(5).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            forestBridge.GetChild(1).GetChild(6).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            forestBridge.GetChild(1).GetChild(7).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            forestBridge.GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;

            Transform platform2_1 = templePieces.GetChild(2).transform;
            platform2_1.GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_1.GetChild(9).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_1.GetChild(9).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;



            Transform platform2_1_2 = platform2_1.GetChild(0).transform;
            platform2_1_2.GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_1_2.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_1_2.GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_1_2.GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_1_2.GetChild(4).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_1_2.GetChild(4).GetChild(22).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;

            platform2_1.GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_1.GetChild(6).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_1.GetChild(8).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;

            Transform platform2_2 = templePieces.GetChild(3).transform;
            platform2_2.GetComponent<MeshRenderer>().material = lavaMaterialScaled07;

            Transform platform2_2_bridge = platform2_2.GetChild(4).transform;
            platform2_2_bridge.GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_2_bridge.GetChild(12).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_2_bridge.GetChild(12).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_2_bridge.GetChild(12).GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_2_bridge.GetChild(12).GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;

            Transform platform2_2_2 = platform2_2.GetChild(5).transform;
            platform2_2_2.GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_2_2.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_2_2.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_2_2.GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_2_2.GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;

            platform2_2_2.GetChild(3).GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_2_2.GetChild(3).GetChild(18).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;

            platform2_2.GetChild(9).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_2.GetChild(10).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_2.GetChild(11).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_2.GetChild(12).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_2.GetChild(13).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_2.GetChild(14).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
            platform2_2.GetChild(33).GetComponent<MeshRenderer>().material = lavaMaterialScaled07;
        }

        private void SulfurPools()
        {
            // DONE

            Material lavaMaterialScaledPod = new Material(lavaMaterial);
            lavaMaterialScaledPod.mainTextureScale = new Vector2(0.5f, 0.5f);

            Transform terrain = GameObject.Find("mdlSPTerrain").transform;
            terrain.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(4).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(8).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(9).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(11).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(13).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(16).gameObject.layer = 0;
            terrain.GetChild(16).gameObject.GetComponent<MeshCollider>().convex = false;
            terrain.GetChild(17).gameObject.layer = 0;
            //terrain.GetChild(17).gameObject.GetComponent<MeshCollider>().convex = false;
            terrain.GetChild(17).gameObject.GetComponent<MeshCollider>().isTrigger = true;
            terrain.GetChild(18).gameObject.layer = 0;
            terrain.GetChild(18).gameObject.GetComponent<MeshCollider>().convex = false;
            terrain.GetChild(19).gameObject.layer = 0;
            terrain.GetChild(19).gameObject.GetComponent<MeshCollider>().convex = false;

            Transform zones = GameObject.Find("HOLDER: Zones").transform;
            Transform pp = zones.transform.GetChild(0).transform;

            for (int i = 0; i < pp.childCount; i++)
            {
                //pp.GetChild(i).gameObject.GetComponent<MeshCollider>().convex = false;
                if(i > 6 && i < 25)
                {
                    pp.GetChild(i).gameObject.layer = 0;
                    pp.GetChild(i).gameObject.GetComponent<MeshCollider>().isTrigger = false;
                }
                
            }

            Transform sulfurPods = GameObject.Find("HOLDER: SulfurPods").transform;
            for (int i = 0; i < sulfurPods.childCount; i++)
            {
                sulfurPods.GetChild(i).GetChild(1).GetComponent<GenericSceneSpawnPoint>().networkedObjectPrefab.GetComponentInChildren<MeshRenderer>().material = lavaMaterialScaledPod;
            }

        }

        private void TreebornColony()
        {
            // DONE

            Transform playableArea = GameObject.Find("HOLDER: Playable Area").transform;
            playableArea.GetChild(2).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            playableArea.GetChild(2).GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;
            playableArea.GetChild(2).GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterial;

            GameObject.Find("HOLDER: SW Section TG").transform.GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterial;


            GameObject.Find("HOLDER: Colossus Statue").transform.GetChild(1).GetChild(4).GetComponent<MeshRenderer>().material = lavaMaterial;
            if (GameObject.Find("HOLDER: Colossus Statue").transform.GetChild(1).childCount > 7)
            {
                GameObject.Find("HOLDER: Colossus Statue").transform.GetChild(1).GetChild(8).gameObject.AddComponent<MeshCollider>();
                GameObject.Find("HOLDER: Colossus Statue").transform.GetChild(1).GetChild(8).gameObject.layer = 0;
            }
            
        }

        private void AbyssalDepths()
        {

            Material lavaMaterialScaled50 = new Material(lavaMaterial);
            lavaMaterialScaled50.mainTextureScale = new Vector2(900f, 900f);



            Transform gameplaySpace = GameObject.Find("HOLDER: Gameplay Space").transform;

            Transform terrainColumns = gameplaySpace.GetChild(3).transform;
            for (int i = 0; i < terrainColumns.childCount; i++)
            {
                terrainColumns.GetChild(i).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
            }

            gameplaySpace.GetChild(5).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
            gameplaySpace.GetChild(6).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
            gameplaySpace.GetChild(7).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
            gameplaySpace.GetChild(8).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
            gameplaySpace.GetChild(9).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;


            MeshRenderer[] array = Object.FindObjectsOfType(typeof(MeshRenderer)) as MeshRenderer[];
            MeshRenderer[] array2 = array;
            foreach (MeshRenderer val2 in array2)
            {
                GameObject gameObject = ((Component)val2).gameObject;
                if ((Object)(object)gameObject != (Object)null)
                {
                    if (((gameObject.name.Contains("DCHero") || gameObject.name.Contains("DCTerrain")) && !gameObject.name.Contains("DCHeroPillar") && !gameObject.name.Contains("DCHeroSwitchback") && (Object)(object)((Renderer)val2).material))
                    {
                        ((Renderer)val2).material = lavaMaterialScaled50;
                    }
                }
            }

        }

        private void SirensCall()
        {
            Transform environment = GameObject.Find("HOLDER: Environment").transform;
            Transform terrain = environment.GetChild(1).transform;

            for (int i = 0; i < terrain.childCount; i++)
            {
                terrain.GetChild(i).GetComponent<MeshRenderer>().material = lavaMaterial;
            }

            Transform cave = environment.GetChild(2).transform;
            cave.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            cave.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;
            cave.GetChild(10).GetComponent<MeshRenderer>().material = lavaMaterial;
            cave.GetChild(11).GetComponent<MeshRenderer>().material = lavaMaterial;
            cave.GetChild(12).GetComponent<MeshRenderer>().material = lavaMaterial;
            cave.GetChild(13).GetComponent<MeshRenderer>().material = lavaMaterial;
            cave.GetChild(15).GetComponent<MeshRenderer>().material = lavaMaterial;
            cave.GetChild(16).GetComponent<MeshRenderer>().material = lavaMaterial;
            cave.GetChild(17).GetComponent<MeshRenderer>().material = lavaMaterial;
            cave.GetChild(18).GetComponent<MeshRenderer>().material = lavaMaterial;
            cave.GetChild(19).GetComponent<MeshRenderer>().material = lavaMaterial;

            environment.GetChild(6).GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;
            environment.GetChild(6).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            environment.GetChild(7).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            environment.GetChild(8).GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterial;

            Transform distantTerrain = GameObject.Find("HOLDER: Skybox + OOB").transform;
            distantTerrain.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            distantTerrain.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;

        }

        private void SunderedGrove()
        {
            Transform terrain = GameObject.Find("HOLDER: Terrain").transform;
            terrain.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterial;

            Transform random = GameObject.Find("HOLDER: Randomization").transform;
            random.GetChild(1).GetChild(0).GetChild(72).GetComponent<MeshRenderer>().material = lavaMaterial;
            random.GetChild(1).GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            random.GetChild(2).GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            random.GetChild(2).GetChild(1).GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;
            random.GetChild(2).GetChild(1).GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterial;
            random.GetChild(2).GetChild(1).GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterial;
            random.GetChild(2).GetChild(1).GetChild(4).GetComponent<MeshRenderer>().material = lavaMaterial;

            GameObject.Find("HOLDER: Props").transform.GetChild(4).GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterial;
        }

        private void PrimeMeridian()
        {

            Material lavaMaterialScaled1 = new Material(lavaMaterial);
            lavaMaterialScaled1.mainTextureScale = new Vector2(1, 1);

            Material lavaMaterialScaled02 = new Material(lavaMaterial);
            lavaMaterialScaled02.mainTextureScale = new Vector2(0.02f, 0.02f);

            Transform terrain = GameObject.Find("Terrain").transform;
            terrain.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled1;
            terrain.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled1;

            Transform art = GameObject.Find("HOLDER: Art").transform;

            Material[] m = art.GetChild(2).GetChild(0).GetComponent<MeshRenderer>().materials;
            m[0] = lavaMaterialScaled1;

            art.GetChild(2).GetChild(0).GetComponent<MeshRenderer>().materials = m;

            Material[] m1 = art.GetChild(4).GetComponent<MeshRenderer>().materials;
            m1[0] = lavaMaterialScaled02;

            art.GetChild(4).GetComponent<MeshRenderer>().materials = m1;

            Material[] m2 = art.GetChild(5).GetComponent<MeshRenderer>().materials;
            m2[1] = lavaMaterialScaled02;

            art.GetChild(5).GetComponent<MeshRenderer>().materials = m2;


            terrain.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled1;
        }

        // Stage 5
        private void HelminthHatchery()
        {
            Transform terrain = GameObject.Find("HOLDER: Art").transform;
            terrain.GetChild(4).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
        }


        private void SkyMeadow()
        {
            Material lavaMaterialScaled50 = new Material(lavaMaterial);
            lavaMaterialScaled50.mainTextureScale = new Vector2(50f, 50f);

            if(easySkyMeadows.Value)
            {
                Transform terrain = GameObject.Find("HOLDER: Terrain").transform;
                for (int i = 19; i < terrain.childCount; i++)
                {
                    terrain.GetChild(i).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                }

                Transform randomization = GameObject.Find("HOLDER: Randomization").transform;
                randomization.GetChild(4).GetChild(0).GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                randomization.GetChild(4).GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                randomization.GetChild(4).GetChild(1).GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                randomization.GetChild(4).GetChild(1).GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                randomization.GetChild(4).GetChild(1).GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                GameObject.Find("PortalDialerEvent").transform.GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

            } else
            {
                Transform terrain = GameObject.Find("HOLDER: Terrain").transform;
                for (int i = 0; i < terrain.childCount; i++)
                {
                    terrain.GetChild(i).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                }

                Transform randomization = GameObject.Find("HOLDER: Randomization").transform;
                randomization.GetChild(0).GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                randomization.GetChild(0).GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                randomization.GetChild(1).GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                randomization.GetChild(1).GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                randomization.GetChild(2).GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                randomization.GetChild(3).GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                randomization.GetChild(4).GetChild(0).GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                randomization.GetChild(4).GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                randomization.GetChild(4).GetChild(1).GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                randomization.GetChild(4).GetChild(1).GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                randomization.GetChild(4).GetChild(1).GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                randomization.GetChild(5).GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                randomization.GetChild(5).GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                GameObject.Find("PortalDialerEvent").transform.GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

            }
        }

        private void Moon2()
        {

            Material lavaMaterialScaled50 = new Material(lavaMaterial);
            lavaMaterialScaled50.mainTextureScale = new Vector2(900f, 900f);

            GameObject.Find("OOB Objects").transform.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;
            GameObject.Find("OOB Objects").transform.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;

            MeshRenderer[] array = Object.FindObjectsOfType(typeof(MeshRenderer)) as MeshRenderer[];
            MeshRenderer[] array2 = array;
            foreach (MeshRenderer val2 in array2)
            {
                GameObject gameObject = ((Component)val2).gameObject;
                if ((Object)(object)gameObject != (Object)null)
                {
                    if (((gameObject.name.Contains("Terrain") || gameObject.name == "HG_Q1_OuterRing_Cave" || gameObject.name == "MoonTempleIsland") && (Object)(object)((Renderer)val2).material))
                    {
                        ((Renderer)val2).material = lavaMaterial;

                        if (gameObject.GetComponent<MeshFilter>())
                        {
                            gameObject.GetComponent<MeshFilter>().mesh.RecalculateNormals();
                        }
                        else
                        {
                            gameObject.AddComponent<MeshFilter>();
                            gameObject.GetComponent<MeshFilter>().mesh = lavaMesh;
                            gameObject.GetComponent<MeshFilter>().mesh.RecalculateNormals();
                        }


                        //gameObject.GetComponent<SurfaceDefProvider>().surfaceDef = lavaDef;
                    }
                }
            }

            GameObject.Find("HOLDER: Gameplay Space").transform.GetChild(0).GetChild(0).GetChild(1).GetChild(5).GetComponent<MeshRenderer>().material = lavaMaterial;
            GameObject.Find("HOLDER: Gameplay Space").transform.GetChild(0).GetChild(4).GetChild(0).GetChild(2).GetChild(5).GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterial;
            GameObject.Find("HOLDER: Gameplay Space").transform.GetChild(0).GetChild(4).GetChild(0).GetChild(2).GetChild(5).GetChild(3).gameObject.AddComponent<MeshCollider>();


            Transform arena = GameObject.Find("HOLDER: Final Arena").transform;
            // Inner bowl
            /*arena.GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            arena.GetChild(0).GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;*/
            /* Transform octagonPlates = arena.GetChild(5).transform;
             for (int i = 0; i < octagonPlates.childCount; i++)
             {
                 octagonPlates.GetChild(i).GetComponent<MeshRenderer>().material = lavaMaterial;
             }*/
            arena.GetChild(8).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            arena.GetChild(8).GetChild(0).gameObject.AddComponent<MeshCollider>();
        }

        private void MomentFractured()
        {
            Transform terrain = GameObject.Find("HOLDER: Gameplay Space").transform;
            for (int i = 0; i < terrain.childCount; i++)
            {
                if (i != 5)
                {
                    terrain.GetChild(i).GetComponent<MeshRenderer>().material = lavaMaterial;
                }
            }
        }

        private void Bazaar()
        {
            Transform cave = GameObject.Find("HOLDER: Starting Cave").transform;
            cave.GetChild(2).GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;
            cave.GetChild(2).GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterial;
        }

        private void GildedCoast()
        {
            Material lavaMaterialScaled50 = new Material(lavaMaterial);
            lavaMaterialScaled50.mainTextureScale = new Vector2(50, 50);

            GameObject.Find("HOLDER: Water").transform.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
            GameObject.Find("HOLDER: Water").transform.GetChild(0).gameObject.AddComponent<MeshCollider>();
        }

        private void VoidFields()
        {
            Transform terrain = GameObject.Find("HOLDER: Terrain").transform;
            terrain.GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            terrain.GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;
        }

        private void VoidLocus()
        {
            Transform islands = GameObject.Find("HOLDER: Terrain").transform.GetChild(2).GetChild(6).transform;
            islands.GetChild(0).GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterial;
            islands.GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            islands.GetChild(2).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            islands.GetChild(3).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            islands.GetChild(4).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
            islands.GetChild(5).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;
        }

        private void Planetarium()
        {
            if(GameObject.Find("RaidBB"))
            {
                GameObject.Find("RaidBB").transform.GetChild(3).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;

            }
            if (GameObject.Find("RaidGP"))
            {
                GameObject.Find("RaidGP").transform.GetChild(3).GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterial;

            }
            if (GameObject.Find("RaidSG"))
            {
                GameObject.Find("RaidSG").transform.GetChild(3).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;

            }
            if (GameObject.Find("RaidDC"))
            {
                GameObject.Find("RaidDC").transform.GetChild(4).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;

            }
            if (GameObject.Find("RaidAL"))
            {
                GameObject.Find("RaidAL").transform.GetChild(3).GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterial;

            }
            if (GameObject.Find("RaidVoid"))
            {
                GameObject.Find("RaidVoid").transform.GetChild(2).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;

            }
        }

        private void VoidStageNewDonut(On.RoR2.VoidRaidGauntletController.orig_RpcActivateDonut orig, VoidRaidGauntletController self, int donutIndex)
        {
            orig(self, donutIndex);
            if (GameObject.Find("RaidBB"))
            {
                GameObject.Find("RaidBB").transform.GetChild(3).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;

            }
            if (GameObject.Find("RaidGP"))
            {
                GameObject.Find("RaidGP").transform.GetChild(3).GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterial;

            }
            if (GameObject.Find("RaidSG"))
            {
                GameObject.Find("RaidSG").transform.GetChild(3).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;

            }
            if (GameObject.Find("RaidDC"))
            {
                GameObject.Find("RaidDC").transform.GetChild(4).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;

            }
            if (GameObject.Find("RaidAL"))
            {
                GameObject.Find("RaidAL").transform.GetChild(3).GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterial;

            }
            if (GameObject.Find("RaidVoid"))
            {
                GameObject.Find("RaidVoid").transform.GetChild(2).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterial;

            }
        }

        private void Update()
        {
            if (Run.instance == null)
            {
                if(inLavaDuration.Count != 0)
                {
                    inLavaDuration = new Dictionary<CharacterBody, float>();
                }

                Scene activeScene = SceneManager.GetActiveScene();
                if (activeScene.name == "title")
                {
                    Title();
                }
            }
        }

        private void configs()
        {

            takeDamageOnAllGround = Config.Bind("General", "Take Damage From All Ground", false, "If you want to take damage when touching any ground (including lava).\nDefault is false.");
            ModSettingsManager.AddOption(new CheckBoxOption(takeDamageOnAllGround));

            healthDamagePercentage = Config.Bind("General", "Lava Damage Percent", 1f, "Percent damage of full health done per tick of lava damage. Full health includes base health and shield.\nDefault is 1 (1%).");
            ModSettingsManager.AddOption(new StepSliderOption(healthDamagePercentage,
                new StepSliderConfig
                {
                    min = 0f,
                    max = 100f,
                    increment = 0.01f
                }));

            lavaDurationDamageScaling = Config.Bind("General", "Lava Contact Damage Scaling", 1f, "How much lava damage scales for every extra second you stand in it. Set to 0 to keep lava damage the same.\nDefault is 1 (lava does 1x more damage every second you're in it).");
            ModSettingsManager.AddOption(new StepSliderOption(lavaDurationDamageScaling,
                new StepSliderConfig
                {
                    min = 0f,
                    max = 100f,
                    increment = 0.01f
                }));

            
            lavaCooldown = Config.Bind("General", "Lava Damage Cooldown", 0.2f, "Cooldown in seconds between ticks of lava damage\nDefault is 0.2.");
            ModSettingsManager.AddOption(new StepSliderOption(lavaCooldown,
                new StepSliderConfig
                {
                    min = 0f,
                    max = 10f,
                    increment = 0.05f
                }));

            lavaDamageEnemies = Config.Bind("General", "Lava Damages Enemies", false, "If lava can damage enemies.\nDefault is false.");
            ModSettingsManager.AddOption(new CheckBoxOption(lavaDamageEnemies));

            lavaEnemyDamage = Config.Bind("General", "Lava Enemy Damage Percent", 0.2f, "Percent damage of full health done to enemies per tick of lava damage. Only active if Lava Damages Enemies is true.\nDefault is 0.2 (0.2%).");
            ModSettingsManager.AddOption(new StepSliderOption(lavaEnemyDamage,
                new StepSliderConfig
                {
                    min = 0f,
                    max = 100f,
                    increment = 0.01f
                }));

            lavaCanKillPlayers = Config.Bind("General", "Player Can Die From Lava", false, "If lava damage can take you below 1 health and kill you.\nDefault is false.");
            ModSettingsManager.AddOption(new CheckBoxOption(lavaCanKillPlayers));

            lavaCanKillEnemies = Config.Bind("General", "Enemies Can Die From Lava", true, "If lava damage can take enemies below 1 health and kill them. Only active if Lava Damages Enemies is true.\nDefault is true.");
            ModSettingsManager.AddOption(new CheckBoxOption(lavaCanKillEnemies));

            usePennies = Config.Bind("General", "Trigger Roll of Pennies", true, "If lava damage can trigger and gain gold from Roll of Pennies.\nDefault is true.");
            ModSettingsManager.AddOption(new CheckBoxOption(usePennies));

            penniesPayMultiplier = Config.Bind("General", "Roll of Pennies Pay Multiplier", 0.4f, "How much to multiply lava Rolls of Pennies payments by for balancing.\nDefault is 0.4 (0.4X).");
            ModSettingsManager.AddOption(new StepSliderOption(penniesPayMultiplier,
                new StepSliderConfig
                {
                    min = 0f,
                    max = 5f,
                    increment = 0.01f
                }));

            easySkyMeadows = Config.Bind("General", "Easy Sky Meadows", true, "Leaves most of the floating platforms on Sky Meadows as regular terrain. Turning this setting off will replace them with lava and make this stage very challenging. \nDefault is true.");
            ModSettingsManager.AddOption(new CheckBoxOption(easySkyMeadows));

            easySkyMeadows.SettingChanged += (o, args) => {

                if (Run.instance != null)
                {
                    Scene activeScene = SceneManager.GetActiveScene();
                    if (activeScene.name == "skymeadow")
                    {
                        MeshRenderer[] array = Object.FindObjectsOfType(typeof(MeshRenderer)) as MeshRenderer[];
                        MeshRenderer[] array2 = array;
                        foreach (MeshRenderer val2 in array2)
                        {
                            GameObject gameObject = ((Component)val2).gameObject;
                            if ((Object)(object)gameObject != (Object)null)
                            {
                                if ((val2.material && (val2.material.name == "matHRLava (Instance)" || val2.material.name == "matHRLava" || val2.material.name == "matHRLava (Material)")))
                                {

                                    gameObject.GetComponent<SurfaceDefProvider>().surfaceDef = dirtDef;
                                    gameObject.GetComponent<MeshRenderer>().material = SMMaterial;

                                }
                                else if (gameObject.name.Contains("Decal") || val2.material.name.Contains("Decal") || gameObject.GetComponent<MeshFilter>().mesh.name.Contains("Decal"))
                                {
                                    gameObject.SetActive(false);
                                }
                            }
                        }

                        Material lavaMaterialScaled50 = new Material(lavaMaterial);
                        lavaMaterialScaled50.mainTextureScale = new Vector2(50f, 50f);

                        if (easySkyMeadows.Value)
                        {
                            Transform terrain = GameObject.Find("HOLDER: Terrain").transform;
                            for (int i = 19; i < terrain.childCount; i++)
                            {
                                terrain.GetChild(i).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                            }

                            Transform randomization = GameObject.Find("HOLDER: Randomization").transform;
                            randomization.GetChild(4).GetChild(0).GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                            randomization.GetChild(4).GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                            randomization.GetChild(4).GetChild(1).GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                            randomization.GetChild(4).GetChild(1).GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                            randomization.GetChild(4).GetChild(1).GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                            GameObject.Find("PortalDialerEvent").transform.GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                        }
                        else
                        {
                            Transform terrain = GameObject.Find("HOLDER: Terrain").transform;
                            for (int i = 0; i < terrain.childCount; i++)
                            {
                                terrain.GetChild(i).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                            }

                            Transform randomization = GameObject.Find("HOLDER: Randomization").transform;
                            randomization.GetChild(0).GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                            randomization.GetChild(0).GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                            randomization.GetChild(1).GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                            randomization.GetChild(1).GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                            randomization.GetChild(2).GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                            randomization.GetChild(3).GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                            randomization.GetChild(4).GetChild(0).GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                            randomization.GetChild(4).GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                            randomization.GetChild(4).GetChild(1).GetChild(1).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                            randomization.GetChild(4).GetChild(1).GetChild(2).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                            randomization.GetChild(4).GetChild(1).GetChild(3).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                            randomization.GetChild(5).GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;
                            randomization.GetChild(5).GetChild(1).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                            GameObject.Find("PortalDialerEvent").transform.GetChild(0).GetChild(0).GetComponent<MeshRenderer>().material = lavaMaterialScaled50;

                            MeshRenderer[] array3 = Object.FindObjectsOfType(typeof(MeshRenderer)) as MeshRenderer[];
                            MeshRenderer[] array4 = array3;
                            foreach (MeshRenderer val2 in array4)
                            {
                                GameObject gameObject = ((Component)val2).gameObject;
                                if ((Object)(object)gameObject != (Object)null)
                                {
                                    if ((val2.material && (val2.material.name == "matHRLava (Instance)" || val2.material.name == "matHRLava" || val2.material.name == "matHRLava (Material)")))
                                    {
                                        if (!gameObject.GetComponent<SurfaceDefProvider>())
                                        {
                                            gameObject.AddComponent<SurfaceDefProvider>();
                                        }
                                        gameObject.GetComponent<SurfaceDefProvider>().surfaceDef = lavaDef;
                                        gameObject.GetComponent<SurfaceDefProvider>().surfaceDef.impactEffectPrefab = lavaDef.footstepEffectPrefab;
                                        gameObject.GetComponent<SurfaceDefProvider>().surfaceDef.footstepEffectPrefab = lavaDef.footstepEffectPrefab;

                                    }
                                    else if (gameObject.name.Contains("Decal") || val2.material.name.Contains("Decal") || gameObject.GetComponent<MeshFilter>().mesh.name.Contains("Decal"))
                                    {
                                        gameObject.SetActive(false);
                                    }
                                }
                            }
                        }
                    }
                }

                
            };

            lavaBrightness = Config.Bind("General", "Lava Brightness", 2f, "How bright the lava will be.\nDefault is 2.");
            ModSettingsManager.AddOption(new StepSliderOption(lavaBrightness,
                new StepSliderConfig
                {
                    min = 0f,
                    max = 3f,
                    increment = 0.01f
                }));

            lavaBrightness.SettingChanged += (o, args) =>
            {
                UpdateSceneMaterials();
            };
        }

    }
}
