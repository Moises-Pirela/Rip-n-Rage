using System.Collections.Generic;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// Builds the PlayerCharacter hierarchy from Assets/_Game/Prefabs/PlayerCharacter.prefab:
    ///
    /// PlayerCharacter [CharacterController h1.8 r0.5, AudioSource, FPSController, PlayerInputActions,
    ///                  FPSCameraController, PlayerState, CombatController, HudController, RagePostprocess]
    ///  ├ Orientation
    ///  ├ Camera (0, 0.5, 0) [HeadBob]
    ///  │   └ WorldCamera (fov 77) [Camera, CameraRecoil]
    ///  │       ├ PlayerArms (0.017, -0.65, 2.36) [WeaponSway]
    ///  │       │    ├ Weapon (scale 0.1) [SpriteRenderer, Animator, WeaponAnimationEvents, AudioSource]
    ///  │       │    │    ├ MuzzleFlash (-0.5, 4.5, 0.05) scale 0.5 (inactive)
    ///  │       │    │    └ ProjectileSpawn (1, 0, 0)
    ///  │       │    ├ LeftFist (-0.35, 0.6, -0.8) scale (0.1, 0.1, 0.075) [SpriteRenderer, Animator]
    ///  │       │    ├ RightFist (0.35, 0.6, -0.8)
    ///  │       │    └ FistAudio [AudioSource]
    ///  │       └ Muzzle_01 (0, 0, 2)
    ///  ├ WeaponHolder (0, 0.8, 2)
    ///  ├ Feet (0, -0.8, 0)
    ///  └ PlayerHUD (canvas 1920x1080)
    /// </summary>
    public static class PlayerPrefab
    {
        public static readonly Prefab Player = new Prefab("PlayerCharacter", Build);

        private static GameObject Build()
        {
            var player = new GameObject("PlayerCharacter");
            player.tag = "Player";
            player.layer = Layers.Player;

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.5f; cc.skinWidth = 0.08f; cc.stepOffset = 0.3f; cc.center = Vector3.zero;

            var footAudio = player.AddComponent<AudioSource>();

            // ---- hierarchy ----
            var orientation = Child(player, "Orientation", Vector3.zero);
            var cameraGo = Child(player, "Camera", new Vector3(0, 0.5f, 0));
            var worldCamera = Child(cameraGo, "WorldCamera", Vector3.zero);
            worldCamera.tag = "MainCamera";
            var arms = Child(worldCamera, "PlayerArms", new Vector3(0.017f, -0.65f, 2.36f));
            var weapon = Child(arms, "Weapon", Vector3.zero, Vector3.one * 0.1f);
            var muzzleFlash = Child(weapon, "MuzzleFlash", new Vector3(-0.5f, 4.5f, 0.05f), Vector3.one * 0.5f);
            var projectileSpawn = Child(weapon, "ProjectileSpawn", new Vector3(1, 0, 0));
            var leftFist = Child(arms, "LeftFist", new Vector3(-0.35f, 0.6f, -0.8f), new Vector3(0.1f, 0.1f, 0.075f));
            var rightFist = Child(arms, "RightFist", new Vector3(0.35f, 0.6f, -0.8f), new Vector3(0.1f, 0.1f, 0.075f));
            var fistAudioGo = Child(arms, "FistAudio", Vector3.zero);
            Child(worldCamera, "Muzzle_01", new Vector3(0, 0, 2));
            Child(player, "WeaponHolder", new Vector3(0, 0.8f, 2));
            Child(player, "Feet", new Vector3(0, -0.8f, 0));

            // ---- camera ----
            var cam = worldCamera.AddComponent<Camera>();
            cam.fieldOfView = 77f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 2000f;
            Camera.main = cam;
            worldCamera.AddComponent<CameraRecoil>().recoilCameraShake = new Vector3(0.5f, 0.5f, 1.5f);

            // ---- first-person sprites ----
            var weaponSr = weapon.AddComponent<SpriteRenderer>();
            weaponSr.sprite = GameAssets.Glock["Glock01"];
            weaponSr.renderLayer = RenderLayer.FirstPerson;
            var weaponAnimator = weapon.AddComponent<Animator>();
            var weaponEvents = weapon.AddComponent<WeaponAnimationEvents>();
            weaponEvents.muzzleFlashObject = muzzleFlash;
            weaponEvents.weaponAudio = weapon.AddComponent<AudioSource>();

            var flashSr = muzzleFlash.AddComponent<SpriteRenderer>();
            flashSr.sprite = GameAssets.Glock["GlockFlash"];
            flashSr.renderLayer = RenderLayer.FirstPerson;
            flashSr.fullBright = true;
            muzzleFlash.SetActive(false);

            var leftSr = leftFist.AddComponent<SpriteRenderer>();
            leftSr.sprite = GameAssets.Fist["fist_l_01"];
            leftSr.renderLayer = RenderLayer.FirstPerson;
            leftSr.sortingOrder = 1;
            var leftAnim = leftFist.AddComponent<Animator>();
            leftAnim.runtimeAnimatorController = GameAssets.LeftFistController;

            var rightSr = rightFist.AddComponent<SpriteRenderer>();
            rightSr.sprite = GameAssets.Fist["fist_r_01"];
            rightSr.renderLayer = RenderLayer.FirstPerson;
            rightSr.sortingOrder = 1;
            var rightAnim = rightFist.AddComponent<Animator>();
            rightAnim.runtimeAnimatorController = GameAssets.RightFistController;

            var fistAudio = fistAudioGo.AddComponent<AudioSource>();

            // ---- sway / bob (values from the prefab) ----
            var sway = arms.AddComponent<WeaponSway>();
            sway.swayAmount = 0.01f; sway.swaySpeed = 20f; sway.horizontalSwayAmount = 0.01f; sway.horizontalSwaySpeed = 10f;
            sway.zSmoothLerp = 12f; sway.zSwayAngle = 5f; sway.zSwayMultiplier = -100f; sway.zSmoothSway = 20f;

            var bob = cameraGo.AddComponent<HeadBob>();
            bob.frequency = 15f; bob.waveLength = 0.1f; bob.swayAmount = 20f; bob.swaySpeed = 1f;

            // ---- player scripts ----
            var input = player.AddComponent<PlayerInputActions>();

            var fps = player.AddComponent<FPSController>();
            fps.m_FootstepSounds = (AudioClip[])GameAssets.Footsteps.Clone();
            fps.m_JumpSound = GameAssets.Jump; fps.m_LandSound = GameAssets.Land;
            fps.m_RunstepLenghten = 0f; fps.m_StepInterval = 2f;
            fps.bEnableBunnyHop = true; fps.crouchSpeed = 1f; fps.walkSpeed = 12f; fps.runSpeed = 16f; fps.bunnyHopSpeed = 30f; fps.jumpSpeed = 20f;
            fps.moveBackwardsSpeedPercent = 1f; fps.moveSideSpeedPercent = 1f;
            fps.lowLandAmount = 0.1f; fps.highLandAmount = 0.6f; fps.landTimer = 0.5f; fps.landDuration = 1f;
            fps.gravityMultiplier = 2.5f; fps.stickToGroundForce = 5f; fps.groundLayer = 3895; fps.rayLength = 0.1f; fps.raySphereRadius = 0.1f;
            fps.obstacleLayers = 2304; fps.rayObstacleLength = 0.1f; fps.rayObstacleSphereRadius = 0.1f;
            fps.smoothRotateSpeed = 5f; fps.smoothInputSpeed = 5f; fps.smoothVelocitySpeed = 5f; fps.smoothFinalDirectionSpeed = 5f;
            sway.controller = fps;
            bob.controller = fps;

            var camCtrl = player.AddComponent<FPSCameraController>();
            camCtrl.playerCamera = cameraGo.transform;
            camCtrl.orientation = orientation.transform;
            camCtrl.sensitivityMultiplier = 1f; camCtrl.sensitivity = 5f;
            camCtrl.zSmoothLerp = 0f; camCtrl.zSwayAngle = 5f; camCtrl.zSwayMultiplier = 100f; camCtrl.zSmoothSway = -50f;

            var state = player.AddComponent<PlayerState>();
            state.rageStates = new[] { GameAssets.NormalRage, GameAssets.EnragedRage, GameAssets.DepressedRage };

            var combat = player.AddComponent<CombatController>();
            combat.cameraTransform = worldCamera.transform;
            combat.projectileSpawnTransform = projectileSpawn.transform;
            combat.layerMask = 6421; // prefab value: Default | Water | Enemy | Environment | Pickup-ish bits
            combat.weaponAnimator = weaponAnimator;
            combat.leftFistAnimator = leftAnim;
            combat.rightFistAnimator = rightAnim;
            combat.jabLandSound = GameAssets.MeleeHit;
            combat.jabSource = fistAudio;
            combat.inventory = new Inventory
            {
                allWeaponPrefabs = new List<Prefab> { Prefabs.Glock, Prefabs.M1887, Prefabs.MG338, Prefabs.RocketLauncher },
                ammoLimits = new[] { 120, 60, 120, 100, 10 },
            };

            // ---- muzzle flash light (not in the Unity prefab; feeds the new lighting) ----
            var muzzleLightGo = Child(player, "MuzzleLight", Vector3.zero);
            muzzleLightGo.AddComponent<MuzzleFlashLight>().cameraTransform = worldCamera.transform;

            // ---- HUD (PlayerHUD canvas) ----
            var hud = player.AddComponent<HudController>();
            hud.playerState = state;
            HudLayout.Build(hud, player);
            hud.ragePostprocess = player.AddComponent<RagePostprocess>();

            return player;
        }

        private static GameObject Child(GameObject parent, string name, Vector3 localPos, Vector3? scale = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            if (scale.HasValue) go.transform.localScale = scale.Value;
            go.layer = parent.layer;
            return go;
        }
    }

    /// <summary>
    /// The PlayerHUD canvas (reference resolution 1920x1080) rebuilt from the prefab's RectTransforms:
    /// a portrait ring bottom-left (health = red radial fill, armor = blue radial fill) and the ammo
    /// counter / ammo-type icon bottom-right, plus the crosshair.
    /// </summary>
    public static class HudLayout
    {
        public static void Build(HudController hud, GameObject player)
        {
            var general = GameAssets.HudGeneral;
            var portraits = GameAssets.HudPortrait;
            var ammoIcons = GameAssets.HudAmmo;

            // Status panel: anchored bottom-left, 295x230, pivot (0,0). Portrait centre = (147.5 - 23.92, 115).
            Vector2 statusAnchor = new Vector2(0, 0);
            Vector2 portraitCenter = new Vector2(147.5f - 23.92f, 115f);

            var frame = Image("Frame", general["frame_portrait"], statusAnchor, portraitCenter, new Vector2(190, 190));
            var statusMask = Image("StatusMask", general["bar_lifearmor"], statusAnchor, portraitCenter + new Vector2(-1.5373f, 0), new Vector2(186.9254f, 187.451f));
            var healthBar = Image("HealthBar", general["bar_lifearmor_fill"], statusAnchor, portraitCenter + new Vector2(-1.5373f, 0), new Vector2(186.9254f, 187.451f));
            healthBar.filled = true; healthBar.fillMethod = FillMethod.Radial360; healthBar.fillClockwise = true; healthBar.fillAmount = 0.5f;
            healthBar.color = new Color(0.84705883f, 0.12156863f, 0.09019608f, 1);
            var armorBar = Image("ArmorBar", general["bar_lifearmor_fill"], statusAnchor, portraitCenter + new Vector2(-1.5373f, 0), new Vector2(186.9254f, 187.451f));
            armorBar.filled = true; armorBar.fillMethod = FillMethod.Radial360; armorBar.fillClockwise = false; armorBar.fillAmount = 0.5f;
            armorBar.color = new Color(0.02745098f, 0.4f, 0.9019608f, 1);
            var face = Image("Face", portraits["portrait_default01"], statusAnchor, portraitCenter, new Vector2(140, 140));

            // Ammo panel: anchored bottom-right, 295x230, pivot (1,0). Centre = (-147.5, 115).
            Vector2 ammoAnchor = new Vector2(1, 0);
            Vector2 ammoCenter = new Vector2(-147.5f, 115f);
            var ammoFrame = Image("IMG_AmmoCount_Frame", general["frame_ammo"], ammoAnchor, ammoCenter + new Vector2(6, -52), new Vector2(105, 105));
            var ammoIcon = Image("IMG_AmmoIcon", ammoIcons["ammo_light"], ammoAnchor, new Vector2(-10.120117f, 230f - 61.320114f), new Vector2(135.7598f, 135.7598f), new Vector2(1, 1));

            var ammoText = new UiText { text = "999", fontSize = 26, alignment = TextAnchor.MiddleCenter }; // TMP size 37; the bitmap font is wider
            ammoText.rectTransform.anchor = ammoAnchor;
            ammoText.rectTransform.anchoredPosition = ammoCenter + new Vector2(6 - 2.5f, -52 - 2.5f);
            ammoText.rectTransform.sizeDelta = new Vector2(70, 70);

            hud.images.AddRange(new[] { frame, statusMask, healthBar, armorBar, face, ammoFrame, ammoIcon });
            hud.texts.Add(ammoText);
            hud.healthBar = healthBar;
            hud.armorBar = armorBar;
            hud.playerPortrait = face;
            hud.ammoTypeIcon = ammoIcon;
            hud.ammoText = ammoText;
            hud.ammoTypeIcons = new[] { ammoIcons["ammo_light"], ammoIcons["ammo_heavy"], ammoIcons["ammo_shell"], ammoIcons["ammo_energy"], ammoIcons["ammo_misile"] };

            hud._portraitData = new PlayerPortraitData
            {
                deathPortrait = portraits["portrait_death01"],
                defaultPortrait = new[] { portraits["portrait_default01"], portraits["portrait_default_damage01"], portraits["portrait_default_damageb01"], portraits["portrait_default_damagec01"], portraits["portrait_default_damaged01"] },
                enragedPortrait = new[] { portraits["portrait_rage01"], portraits["portrait_rage_damage01"], portraits["portrait_rage_damageb01"], portraits["portrait_rage_damagec01"], portraits["portrait_rage_damaged01"] },
                depressedPortrait = new[] { portraits["portrait_sad01"], portraits["portrait_sad_damage01"], portraits["portrait_sad_damageb01"], portraits["portrait_sad_damagec01"], portraits["portrait_sad_damaged01"] },
                inCombatPortrait = new[] { portraits["portrait_fight01"], portraits["portrait_fight_damage01"], portraits["portrait_fight_damageb01"], portraits["portrait_fight_damagec01"], portraits["portrait_fight_damaged01"] },
            };

            // Crosshair object (GlockCrosshair): outer ring 10x10 (frame_portrait sprite), inner 5x5 white square.
            var crosshairGo = new GameObject("Crosshair");
            crosshairGo.transform.SetParent(player.transform, false);
            var crosshair = crosshairGo.AddComponent<GlockCrosshair>();
            crosshair.CrosshairImage.sprite = general["frame_portrait"];
            crosshair.innerCrosshairImage.sprite = general["white-square-th"];
            hud.crossHairObject = crosshairGo;
        }

        private static UiImage Image(string name, Sprite sprite, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var img = new UiImage(name) { sprite = sprite };
            img.rectTransform.anchor = anchor;
            img.rectTransform.anchoredPosition = pos;
            img.rectTransform.sizeDelta = size;
            if (pivot.HasValue) img.rectTransform.pivot = pivot.Value;
            return img;
        }
    }
}
