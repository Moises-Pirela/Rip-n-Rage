using System.Collections.Generic;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// Loads the art/audio from the original Unity <c>Assets/</c> folder and rebuilds, in code, the
    /// data that lived in Unity-only assets: animation clips (.anim), animator controllers
    /// (.controller / .overrideController) and ScriptableObjects (WeaponData, RageStateData, EnemyData).
    /// Every value below was read from the corresponding asset file.
    /// </summary>
    public static class GameAssets
    {
        // ---- atlases ----
        public static SpriteAtlas Glock, M1887, M1887Reload, M1887Special, MG338, RocketLauncher, Fist, Katana;
        public static SpriteAtlas HudGeneral, HudPortrait, HudAmmo, Pickups;
        public static Sprite[] SonicShotgun;

        // ---- textures ----
        public static Texture2D GridTexture, OrangeProto, GreenProto, PurpleProto, RedProto, Texture13;
        public static Texture2D RiflemanIdle, RiflemanAttack, RiflemanRun, RiflemanDeath, RiflemanDead;

        // ---- audio ----
        public static AudioClip GlockShot, M1887Shot, MG338Shot, RocketShot, MGPunch, MeleeHit, SonicShot, SwordSlash;
        public static AudioClip[] Footsteps;
        public static AudioClip Jump, Land;

        // ---- animation ----
        public static AnimatorController BaseWeaponController;
        public static AnimatorController GlockController, M1887Controller, MG338Controller, RocketLauncherController;
        public static AnimatorController LeftFistController, RightFistController, ExplosionController;

        // ---- scriptable data ----
        public static WeaponData GlockData, M1887Data, MG338Data, RocketLauncherData;
        public static RageStateData NormalRage, EnragedRage, DepressedRage;
        public static EnemyData RiflemanData;

        public static bool Loaded { get; private set; }

        public static void Load()
        {
            if (Loaded) return;
            LoadSprites();
            LoadTextures();
            LoadAudio();
            BuildAnimation();
            BuildData();
            RegisterAnimation();
            Loaded = true;
        }

        /// <summary>Explicit animations folder (--animations); otherwise the nearest "animations" directory is used.</summary>
        public static string AnimationsPath;

        /// <summary>
        /// Registers every atlas with <see cref="SpriteLibrary"/> and every clip/controller with <see cref="AnimationLibrary"/>,
        /// then loads the JSON files from the animations folder (which update the code-built defaults in place).
        /// </summary>
        private static void RegisterAnimation()
        {
            SpriteLibrary.RegisterAtlas("Glock", Glock);
            SpriteLibrary.RegisterAtlas("M1887", M1887);
            SpriteLibrary.RegisterAtlas("M1887_Reload", M1887Reload);
            SpriteLibrary.RegisterAtlas("M1887_Special", M1887Special);
            SpriteLibrary.RegisterAtlas("MG338", MG338);
            SpriteLibrary.RegisterAtlas("RocketLauncher", RocketLauncher);
            SpriteLibrary.RegisterAtlas("Fist", Fist);
            SpriteLibrary.RegisterAtlas("Katana", Katana);
            SpriteLibrary.RegisterAtlas("Pickups", Pickups);
            SpriteLibrary.RegisterAtlas("HUD_General", HudGeneral);
            SpriteLibrary.RegisterAtlas("HUD_Portrait", HudPortrait);
            SpriteLibrary.RegisterAtlas("HUD_0", HudAmmo);
            for (int i = 0; i < SonicShotgun.Length; i++) SpriteLibrary.RegisterSprite($"SonicShotgun/Sonic{i + 1:00}", SonicShotgun[i]);

            // preview rigs mirror PlayerPrefab: weapons sit under PlayerArms at WeaponData.weaponPosition/scale
            BaseWeaponController.previewRig = new AnimationPreviewRig { position = Vector3.zero, scale = Vector3.one * 0.1f };
            foreach (var wd in new[] { GlockData, M1887Data, MG338Data, RocketLauncherData })
                wd.animatorOverrideController.previewRig = new AnimationPreviewRig { position = wd.weaponPosition, scale = Vector3.one * wd.weaponScale };
            LeftFistController.previewRig = new AnimationPreviewRig { position = new Vector3(-0.35f, 0.6f, -0.8f), scale = new Vector3(0.1f, 0.1f, 0.075f) };
            RightFistController.previewRig = new AnimationPreviewRig { position = new Vector3(0.35f, 0.6f, -0.8f), scale = new Vector3(0.1f, 0.1f, 0.075f) };
            ExplosionController.previewRig = new AnimationPreviewRig { layer = RenderLayer.World, position = Vector3.zero, scale = Vector3.one * 3f, billboard = true };

            foreach (var c in new[] { BaseWeaponController, GlockController, M1887Controller, MG338Controller, RocketLauncherController, LeftFistController, RightFistController, ExplosionController })
                AnimationLibrary.Register(c);

            string dir = AnimationLibrary.Locate(AnimationsPath);
            if (dir != null)
            {
                AnimationLibrary.SetDirectory(dir);
                AnimationLibrary.LoadAll();
            }
            else Debug.LogWarning("No animations folder found — using the code-built clips.");
        }

        /// <summary>Writes the code-built clips and controllers as JSON (the starting point for the animation editor).</summary>
        public static void ExportAnimations(string dir)
        {
            AnimationLibrary.SetDirectory(dir);
            AnimationLibrary.SaveAll();
            Debug.LogWarning($"Exported {AnimationLibrary.Clips.Count} clips and {AnimationLibrary.Controllers.Count} controllers to {dir}");
        }

        private static string A(string rel) => Application.AssetPath("_Game/" + rel);

        private static void LoadSprites()
        {
            Glock = SpriteAtlas.Load(A("Sprites/Weapons/Glock/Glock.tpsheet"));
            M1887 = SpriteAtlas.Load(A("Sprites/Weapons/M1887/M1887.tpsheet"));
            M1887Reload = SpriteAtlas.Load(A("Sprites/Weapons/M1887/M1887_Reload.tpsheet"));
            M1887Special = SpriteAtlas.Load(A("Sprites/Weapons/M1887/M1887_Special.tpsheet"));
            MG338 = SpriteAtlas.Load(A("Sprites/Weapons/MG338/MG338.tpsheet"));
            RocketLauncher = SpriteAtlas.Load(A("Sprites/Weapons/RocketLauncher/RocketLauncher.tpsheet"));
            Fist = SpriteAtlas.Load(A("Sprites/Weapons/Fist/Fist.tpsheet"), 100f, FilterMode.Point); // Fist.png.meta: filterMode 0 (point)
            Katana = SpriteAtlas.Load(A("Sprites/Weapons/Katana/Katana_01.tpsheet"));
            HudGeneral = SpriteAtlas.Load(A("Sprites/HUD/HUD_General.tpsheet"));
            HudPortrait = SpriteAtlas.Load(A("Sprites/HUD/HUD_Portrait.tpsheet"));
            HudAmmo = SpriteAtlas.Load(A("Sprites/HUD/HUD_0.tpsheet"));
            Pickups = SpriteAtlas.Load(A("Sprites/Pickups/Pickups.tpsheet"));

            SonicShotgun = new[]
            {
                Sprite.FromTexture(Texture2D.Load(A("Sprites/Weapons/SonicShotgun/Sonic01.png"))),
                Sprite.FromTexture(Texture2D.Load(A("Sprites/Weapons/SonicShotgun/Sonic02.png"))),
            };
        }

        private static void LoadTextures()
        {
            GridTexture = Texture2D.Load(A("Textures/texture_08.png"), FilterMode.Trilinear, TextureWrapMode.Repeat);
            Texture13 = Texture2D.Load(A("Textures/texture_13.png"), FilterMode.Trilinear, TextureWrapMode.Repeat);
            OrangeProto = Texture2D.Load(A("Textures/TEX-Orange_Proto.png"), FilterMode.Trilinear, TextureWrapMode.Repeat);
            GreenProto = Texture2D.Load(A("Textures/TEX-Green_Proto.png"), FilterMode.Trilinear, TextureWrapMode.Repeat);
            PurpleProto = Texture2D.Load(A("Textures/TEX-Purple_Proto.png"), FilterMode.Trilinear, TextureWrapMode.Repeat);
            RedProto = Texture2D.Load(A("Textures/TEX-Red_Proto.png"), FilterMode.Trilinear, TextureWrapMode.Repeat);

            RiflemanIdle = Texture2D.Load(A("Sprites/Enemies/Rifleman/Rifleman_Idle.png"));
            RiflemanAttack = Texture2D.Load(A("Sprites/Enemies/Rifleman/Rifleman_Attack.png"));
            RiflemanRun = Texture2D.Load(A("Sprites/Enemies/Rifleman/Rifleman_Run.png"));
            RiflemanDeath = Texture2D.Load(A("Sprites/Enemies/Rifleman/Rifleman_Death.png"));
            RiflemanDead = Texture2D.Load(A("Sprites/Enemies/Rifleman/Rifleman_Dead.png"));
        }

        private static void LoadAudio()
        {
            GlockShot = AudioClip.Load(A("Audio/GlockShot.wav"));
            M1887Shot = AudioClip.Load(A("Audio/Combat/m1887.wav"));
            MG338Shot = AudioClip.Load(A("Audio/Combat/mg338.mp3"));
            RocketShot = AudioClip.Load(A("Audio/Combat/RocketShot.wav"));
            MGPunch = AudioClip.Load(A("Audio/Combat/MGPunch.wav"));
            MeleeHit = AudioClip.Load(A("Audio/Combat/MeleeHit_01.mp3"));
            SonicShot = AudioClip.Load(A("Audio/SonicShotgun.wav"));
            SwordSlash = AudioClip.Load(A("Audio/swordslash1.wav"));
            Footsteps = new[]
            {
                AudioClip.Load(A("Audio/Locomotion/Footstep01.wav")),
                AudioClip.Load(A("Audio/Locomotion/Footstep02.wav")),
                AudioClip.Load(A("Audio/Locomotion/Footstep03.wav")),
                AudioClip.Load(A("Audio/Locomotion/Footstep04.wav")),
            };
            Jump = AudioClip.Load(A("Audio/Locomotion/Jump.wav"));
            Land = AudioClip.Load(A("Audio/Locomotion/Land.wav"));
        }

        // ------------------------------------------------------------------ animation

        private const float F = 1f / 60f; // the clips were authored at 60 samples/second

        private static void BuildAnimation()
        {
            // ---- weapon clips (Assets/_Game/Animation/*) ----
            var glockIdle = new SpriteAnimationClip("GlockIdle", 1 * F).AddFrame(0, Glock["Glock01"]);
            var glockFire = new SpriteAnimationClip("GlockFire", 5 * F)
                .AddFrame(0, Glock["Glock01"]).AddFrame(2 * F, Glock["Glock02"]).AddFrame(4 * F, Glock["Glock01"])
                .AddEvent(0, "OnFire").AddEvent(4 * F, "OnEndFire");
            var glockAltFire = new SpriteAnimationClip("GlockAltFire", 21 * F)
                .AddFrame(0, Glock["Glock01"]).AddFrame(5 * F, Glock["Glock02"]).AddFrame(20 * F, Glock["Glock01"])
                .AddEvent(5 * F, "OnFire").AddEvent(10 * F, "OnGlockBeginAltFire").AddEvent(15 * F, "OnFire").AddEvent(20 * F, "OnEndFire");

            var m1887Idle = new SpriteAnimationClip("M1887_Idle", 1 * F, loop: true).AddFrame(0, M1887["shotgun_01"]);
            var m1887Fire = new SpriteAnimationClip("M1887_Fire", 31 * F, loop: true)
                .AddFrame(0, M1887["shotgun_01"]).AddFrame(1 * F, M1887["shotgun_02"]).AddFrame(2 * F, M1887["shotgun_03"]).AddFrame(3 * F, M1887["shotgun_04"])
                .AddFrame(5 * F, M1887Reload["shotgun_reload_01"]).AddFrame(10 * F, M1887Reload["shotgun_reload_02"]).AddFrame(16 * F, M1887Reload["shotgun_reload_03"])
                .AddFrame(19 * F, M1887Reload["shotgun_reload_04"]).AddFrame(25 * F, M1887Reload["shotgun_reload_05"]).AddFrame(30 * F, M1887Reload["shotgun_reload_06"]);
            var m1887Melee = new SpriteAnimationClip("M1887_Melee", 34 * F)
                .AddFrame(0, M1887["shotgun_01"]).AddFrame(2 * F, M1887["shotgun_02"]).AddFrame(5 * F, M1887["shotgun_03"]).AddFrame(9 * F, M1887["shotgun_04"])
                .AddFrame(12 * F, M1887Special["shotgun_second_01"]).AddFrame(16 * F, M1887Special["shotgun_second_02"]).AddFrame(19 * F, M1887Special["shotgun_second_03"])
                .AddFrame(23 * F, M1887Special["shotgun_second_04"]).AddFrame(26 * F, M1887Special["shotgun_second_05"]).AddFrame(30 * F, M1887Special["shotgun_second_06"])
                .AddFrame(33 * F, M1887["shotgun_01"]);

            var mgIdle = new SpriteAnimationClip("MG_Idle", 1 * F, loop: true).AddFrame(0, MG338["machinegun_01"]);
            var mgFire = new SpriteAnimationClip("MG_Fire", 11 * F)
                .AddFrame(0, MG338["machinegun_01"]).AddFrame(2 * F, MG338["machinegun_02"]).AddFrame(6 * F, MG338["machinegun_03"]).AddFrame(10 * F, MG338["machinegun_04"]);
            var mgMelee = new SpriteAnimationClip("MG_Melee", 11 * F)
                .AddFrame(0, MG338["machinegun_secondary_01"]).AddFrame(2 * F, MG338["machinegun_secondary_02"]).AddFrame(5 * F, MG338["machinegun_secondary_03"])
                .AddFrame(8 * F, MG338["machinegun_secondary_04"]).AddFrame(10 * F, MG338["machinegun_secondary_05"]);

            var rlIdle = new SpriteAnimationClip("RocketLauncherIdle", 1 * F, loop: true).AddFrame(0, RocketLauncher["rocketlauncher _01"]);
            var rlFire = new SpriteAnimationClip("RocketLauncherFire", 9 * F)
                .AddFrame(0, RocketLauncher["rocketlauncher _01"]).AddFrame(2 * F, RocketLauncher["rocketlauncher _02"]).AddFrame(4 * F, RocketLauncher["rocketlauncher _03"])
                .AddFrame(6 * F, RocketLauncher["rocketlauncher _04"]).AddFrame(8 * F, RocketLauncher["rocketlauncher _05"]);

            var baseIdle = new SpriteAnimationClip("BaseIdle", 61 * F).AddFrame(0, Glock["Glock01"]);
            var baseFire = new SpriteAnimationClip("BaseFire", 60 * F);
            var baseMelee = new SpriteAnimationClip("BaseWeaponMelee", 60 * F);

            // ---- BaseWeaponController.controller ----
            // States: Idle(BaseIdle) [default], Fire(BaseFire), WeaponMelee(BaseWeaponMelee), RocketLauncherFire (unreachable).
            // Idle -> Fire when IsFiring; Fire -> Idle when !IsFiring; Idle -> WeaponMelee when WeaponMelee; WeaponMelee -> Idle when !WeaponMelee.
            BaseWeaponController = new AnimatorController("BaseWeaponController")
                .AddParameter("IsFiring", AnimatorParameterType.Bool)
                .AddParameter("WeaponMelee", AnimatorParameterType.Bool);
            var idle = BaseWeaponController.AddState("Idle", "BaseIdle");
            var fire = BaseWeaponController.AddState("Fire", "BaseFire");
            var melee = BaseWeaponController.AddState("WeaponMelee", "BaseWeaponMelee");
            BaseWeaponController.AddState("RocketLauncherFire", "RocketLauncherFire");
            idle.AddTransition(fire).When("IsFiring", AnimatorConditionMode.If);
            fire.AddTransition(idle).When("IsFiring", AnimatorConditionMode.IfNot);
            idle.AddTransition(melee).When("WeaponMelee", AnimatorConditionMode.If);
            melee.AddTransition(idle).When("WeaponMelee", AnimatorConditionMode.IfNot);
            BaseWeaponController.clips["BaseIdle"] = baseIdle;
            BaseWeaponController.clips["BaseFire"] = baseFire;
            BaseWeaponController.clips["BaseWeaponMelee"] = baseMelee;
            BaseWeaponController.clips["RocketLauncherFire"] = rlFire;

            // ---- override controllers ----
            GlockController = BaseWeaponController.CreateOverride("GlockWeaponController", new Dictionary<string, SpriteAnimationClip>
            {
                ["BaseIdle"] = glockIdle, ["BaseFire"] = glockFire, ["BaseWeaponMelee"] = glockAltFire,
            });
            M1887Controller = BaseWeaponController.CreateOverride("M1887WeaponController", new Dictionary<string, SpriteAnimationClip>
            {
                ["BaseIdle"] = m1887Idle, ["BaseFire"] = m1887Fire, ["BaseWeaponMelee"] = m1887Melee,
            });
            MG338Controller = BaseWeaponController.CreateOverride("MGAnimatorController", new Dictionary<string, SpriteAnimationClip>
            {
                ["BaseIdle"] = mgIdle, ["BaseFire"] = mgFire, ["BaseWeaponMelee"] = mgMelee,
            });
            RocketLauncherController = BaseWeaponController.CreateOverride("RocketLauncherController", new Dictionary<string, SpriteAnimationClip>
            {
                ["BaseIdle"] = rlIdle, ["BaseFire"] = rlFire, ["RocketLauncherFire"] = rlFire,
            });

            // ---- fists (LeftFist.controller / RightFist.controller) ----
            var leftIdle = new SpriteAnimationClip("LeftFistIdle", 0f, loop: true).AddEnabled(0, false);
            var leftJab = new SpriteAnimationClip("LeftJab", 3 * F)
                .AddFrame(0, Fist["fist_l_01"]).AddFrame(1 * F, Fist["fist_l_02"]).AddFrame(2 * F, Fist["fist_l_03"])
                .AddEnabled(0, true).AddEnabled(3 * F, false);
            var grab = new SpriteAnimationClip("Grab", 3 * F)
                .AddFrame(0, Fist["fist_l_01"]).AddFrame(1 * F, Fist["fist_l_02"]).AddFrame(2 * F, Fist["grab_01"])
                .AddEnabled(0, true).AddEnabled(3 * F, true);
            var rightIdle = new SpriteAnimationClip("RightFistIdle", 0f, loop: true).AddEnabled(0, false);
            var rightJab = new SpriteAnimationClip("RightJab", 3 * F)
                .AddFrame(0, Fist["fist_r_01"]).AddFrame(1 * F, Fist["fist_r_02"]).AddFrame(2 * F, Fist["fist_r_03"])
                .AddEnabled(0, true).AddEnabled(3 * F, false);

            LeftFistController = new AnimatorController("LeftFist")
                .AddParameter("Jab", AnimatorParameterType.Trigger)
                .AddParameter("Grapple", AnimatorParameterType.Bool);
            var lIdle = LeftFistController.AddState("LeftFistIdle");
            var lJab = LeftFistController.AddState("Jab", "LeftJab"); lJab.speed = 0.5f;
            var lGrab = LeftFistController.AddState("Grab");
            lIdle.AddTransition(lJab).When("Jab", AnimatorConditionMode.If);
            var jabExit = lJab.AddTransition(lIdle); jabExit.hasExitTime = true; jabExit.exitTime = 1f;
            LeftFistController.AddAnyStateTransition(lGrab).When("Grapple", AnimatorConditionMode.If);
            lGrab.AddTransition(lIdle).When("Grapple", AnimatorConditionMode.IfNot);
            LeftFistController.clips["LeftFistIdle"] = leftIdle;
            LeftFistController.clips["LeftJab"] = leftJab;
            LeftFistController.clips["Grab"] = grab;

            RightFistController = new AnimatorController("RightFist").AddParameter("Jab", AnimatorParameterType.Trigger);
            var rIdle = RightFistController.AddState("RightFistIdle");
            var rJab = RightFistController.AddState("Jab", "RightJab"); rJab.speed = 0.5f;
            rIdle.AddTransition(rJab).When("Jab", AnimatorConditionMode.If);
            var rJabExit = rJab.AddTransition(rIdle); rJabExit.hasExitTime = true; rJabExit.exitTime = 1f;
            RightFistController.clips["RightFistIdle"] = rightIdle;
            RightFistController.clips["RightJab"] = rightJab;

            // ---- explosion (RocketExplosion.controller / Explosion.anim) ----
            var explosion = new SpriteAnimationClip("Explosion", 20 * F)
                .AddFrame(0, RocketLauncher["explosion_01"]).AddFrame(5 * F, RocketLauncher["explosion_02"]).AddFrame(15 * F, RocketLauncher["explosion_03"]);
            ExplosionController = new AnimatorController("RocketExplosion");
            ExplosionController.AddState("Explosion");
            ExplosionController.clips["Explosion"] = explosion;
        }

        // ------------------------------------------------------------------ scriptable data

        private static void BuildData()
        {
            // Assets/_Game/ScriptableObjects/RageStates/*.asset
            NormalRage = new RageStateData { name = "NormalStateRageData", movementSpeedModifier = 1f };
            EnragedRage = new RageStateData { name = "EnragedStateRageData", movementSpeedModifier = 1.5f };
            DepressedRage = new RageStateData { name = "DepressedStateRageData", movementSpeedModifier = 0.5f };

            // Assets/_Game/ScriptableObjects/WeaponData/*.asset
            GlockData = new WeaponData
            {
                name = "Glock",
                ammoType = AmmoType.LIGHT, weaponType = WeaponType.SINGLE,
                weaponRecoil = new WeaponRecoil(new Vector3(12, 0, 0), 12, 6),
                weaponStats = new WeaponStats { bulletsPerShot = 2, fireRate = 0.15f, damagePerShot = 100, weaponSpread = 0, isUnlocked = true },
                animatorOverrideController = GlockController,
                fireSound = GlockShot,
                spawnableVFX = Prefabs.BulletImpact,
                weaponPosition = new Vector3(0, 0, 0), weaponScale = 0.1f,
            };
            M1887Data = new WeaponData
            {
                name = "M1887",
                ammoType = AmmoType.SHELL, weaponType = WeaponType.SINGLE,
                weaponRecoil = new WeaponRecoil(new Vector3(12, 0, 0), 12, 6),
                weaponStats = new WeaponStats { bulletsPerShot = 16, fireRate = 0.525f, damagePerShot = 200, weaponSpread = 0.05f, isUnlocked = true },
                animatorOverrideController = M1887Controller,
                fireSound = M1887Shot,
                spawnableVFX = Prefabs.BulletImpact,
                weaponPosition = new Vector3(-0.12f, 0.25f, 0), weaponScale = 0.1f,
            };
            MG338Data = new WeaponData
            {
                name = "MG338",
                ammoType = AmmoType.HEAVY, weaponType = WeaponType.AUTO,
                weaponRecoil = new WeaponRecoil(new Vector3(12, 3, 0), 12, 6),
                weaponStats = new WeaponStats { bulletsPerShot = 1, fireRate = 0.2f, damagePerShot = 1000, weaponSpread = 0, isUnlocked = true },
                animatorOverrideController = MG338Controller,
                fireSound = MG338Shot,
                spawnableVFX = Prefabs.BulletImpact,
                weaponPosition = new Vector3(0.0132f, 0.54f, 0), weaponScale = 0.15f,
            };
            RocketLauncherData = new WeaponData
            {
                name = "RocketLauncher",
                ammoType = AmmoType.EXPLOSIVE, weaponType = WeaponType.SINGLE,
                weaponRecoil = new WeaponRecoil(new Vector3(5, 0, 0), 50, 25),
                weaponStats = new WeaponStats { bulletsPerShot = 1, fireRate = 0.25f, damagePerShot = 1000, weaponSpread = 0, isUnlocked = true },
                animatorOverrideController = RocketLauncherController,
                fireSound = RocketShot,
                spawnableVFX = null,
                weaponPosition = new Vector3(-0.158f, 0.365f, 0), weaponScale = 0.12f,
            };

            // Assets/_Game/ScriptableObjects/EnemyData/RiflemanData.asset
            RiflemanData = new EnemyData
            {
                name = "RiflemanData",
                movementSpeed = 10, attackRange = 10, meleeAttackRange = 2, turnSpeed = 3600, fireRate = 0.75f,
                maxHealth = 1000, maxArmor = 0,
                enemyAnimationData = new EnemyAnimationData
                {
                    IdleAnimation = new AnimationSheet { AnimationFrames = 1, Sides = 4, AnimationFramesPerSecond = 1, TextureSheet = RiflemanIdle },
                    AttackAnimation = new AnimationSheet { AnimationFrames = 4, Sides = 1, AnimationFramesPerSecond = 4, TextureSheet = RiflemanAttack },
                    MoveAnimation = new AnimationSheet { AnimationFrames = 4, Sides = 4, AnimationFramesPerSecond = 5, TextureSheet = RiflemanRun },
                    DeathAnimation = new AnimationSheet { AnimationFrames = 4, Sides = 4, AnimationFramesPerSecond = 5, TextureSheet = RiflemanDeath },
                    DeadAnimation = new AnimationSheet { AnimationFrames = 1, Sides = 4, AnimationFramesPerSecond = 1, TextureSheet = RiflemanDead },
                },
            };
        }
    }
}
