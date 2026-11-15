using RipNRage.Engine.Ecs;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// Builds the player (9.4: entities throughout — the PlayerCharacter GameObject hierarchy and its facade
    /// MonoBehaviours are gone): the character and view rig (<see cref="RipNRage.Game.Ecs.PlayerLink.Spawn"/>), the
    /// first-person viewmodel entities, the weapon tables and components, the muzzle light and the HUD screen.
    /// </summary>
    public static class PlayerPrefab
    {
        public static void Spawn(Vector3 position, float yaw)
        {
            var world = Application.Current.World;

            // ---- the character itself: the force-driven capsule + locomotion + look + view rig ----
            RipNRage.Game.Ecs.PlayerLink.Spawn(world, position, yaw);
            var player = RipNRage.Game.Ecs.PlayerLink.PlayerEntity;

            // ---- first-person viewmodel: entities under the camera entity (arms + WeaponSway, weapon, flash, fists)
            var vm = RipNRage.Game.Ecs.ViewModel.Build(world);
            // the weapon's clip events, by name (9.4: was a SendMessage onto the WeaponAnimationEvents MonoBehaviour)
            vm.Weapon.OnEvent = n =>
            {
                if (n == "OnGlockAltFire") vm.MuzzleFlash.SetActive(true);
                else if (n == "OnGlockBeginAltFire") vm.MuzzleFlash.SetActive(false);
                // "OnFire" / "OnEndFire": nothing listens (the legacy sink was already empty)
            };

            // ---- weapons: definitions as shared data, state as components, firing as a system (5.3) ----
            RipNRage.Game.Ecs.Combat.Weapons.All = new[]
                { GameAssets.GlockData, GameAssets.M1887Data, GameAssets.MG338Data, GameAssets.RocketLauncherData };
            world.Add(player, new RipNRage.Game.Ecs.Combat.Weapon { Sprite = vm.Weapon.Entity });
            world.Add(player, new RipNRage.Game.Ecs.Combat.Inventory());
            world.Add(player, new RipNRage.Game.Ecs.Combat.AltFire
                { LeftFist = vm.LeftFist.Entity, RightFist = vm.RightFist.Entity });

            // weapon switching and the viewmodel equip (9.4: a static — the new-game ammo, weapon 0)
            CombatController.Bind(vm.Weapon);

            // ---- muzzle flash light (not in the Unity prefab; feeds the new lighting): an entity + system (9.3) ----
            RipNRage.Game.Ecs.MuzzleFlashes.Spawn(world);

            // ---- HUD (PlayerHUD canvas): a plain screen reading the components (8.7) ----
            HudLayout.Build(UiScreens.Add(new HudScreen()));
        }
    }

    public static class HudLayout
    {
        public static void Build(HudScreen hud)
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
            hud.healthBar.fillAmount = HudScreen.BarFill(100f); hud.armorBar.fillAmount = HudScreen.BarFill(100f);
            hud.ammoTypeIcons = new[] { ammoIcons["ammo_light"], ammoIcons["ammo_heavy"], ammoIcons["ammo_shell"], ammoIcons["ammo_energy"], ammoIcons["ammo_misile"] };

            hud.portraitData = new PlayerPortraitData
            {
                deathPortrait = portraits["portrait_death01"],
                defaultPortrait = new[] { portraits["portrait_default01"], portraits["portrait_default_damage01"], portraits["portrait_default_damageb01"], portraits["portrait_default_damagec01"], portraits["portrait_default_damaged01"] },
                enragedPortrait = new[] { portraits["portrait_rage01"], portraits["portrait_rage_damage01"], portraits["portrait_rage_damageb01"], portraits["portrait_rage_damagec01"], portraits["portrait_rage_damaged01"] },
                depressedPortrait = new[] { portraits["portrait_sad01"], portraits["portrait_sad_damage01"], portraits["portrait_sad_damageb01"], portraits["portrait_sad_damagec01"], portraits["portrait_sad_damaged01"] },
                inCombatPortrait = new[] { portraits["portrait_fight01"], portraits["portrait_fight_damage01"], portraits["portrait_fight_damageb01"], portraits["portrait_fight_damagec01"], portraits["portrait_fight_damaged01"] },
            };

            // Crosshair (GlockCrosshair): outer ring 10x10 (frame_portrait sprite), inner 5x5 white square.
            hud.Crosshair = new GlockCrosshair();
            hud.Crosshair.CrosshairImage.sprite = general["frame_portrait"];
            hud.Crosshair.innerCrosshairImage.sprite = general["white-square-th"];
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
