/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using HarmonyLib;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Silent Building Blocks", "VisEntities", "1.0.0")]
    [Description("Removes the smoke effect when placing building blocks.")]
    public class SilentBuildingBlocks : RustPlugin
    {
        #region Fields

        private static SilentBuildingBlocks _plugin;
        private Harmony _harmony;

        #endregion Fields

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            _harmony = new Harmony(Name + "PATCH");
            _harmony.PatchAll();
        }

        private void Unload()
        {
            _harmony.UnpatchAll(Name + "PATCH");
            _plugin = null;
        }

        private object OnCustomConstructionPlace(Planner planner, Construction.Target placement, Construction component)
        {
            // Replicate the original 'DoPlacement' method minus the effect.
            BasePlayer ownerPlayer = planner.GetOwnerPlayer();
            if (ownerPlayer == null)
                return null;

            BaseEntity baseEntity = component.CreateConstruction(placement, true);
            if (baseEntity == null)
                return null;

            float conditionMultiplier = 1f;

            // Replicate the logic from 'GetOwnerItem'.
            Item ownerItem = null;
            if (ownerPlayer.inventory != null)
                ownerItem = ownerPlayer.inventory.FindItemByUID((planner as HeldEntity).ownerItemUID);

            if (ownerItem != null)
            {
                baseEntity.skinID = ownerItem.skin;
                if (ownerItem.hasCondition)
                {
                    conditionMultiplier = ownerItem.conditionNormalized;
                }
            }

            baseEntity.gameObject.AwakeFromInstantiate();

            BuildingBlock buildingBlock = baseEntity as BuildingBlock;
            if (buildingBlock != null)
            {
                buildingBlock.blockDefinition = PrefabAttribute.server.Find<Construction>(buildingBlock.prefabID);
                if (buildingBlock.blockDefinition == null)
                {
                    Debug.LogError("Placing a building block that has no block definition!");
                    return null;
                }

                buildingBlock.SetGrade(buildingBlock.blockDefinition.defaultGrade.gradeBase.type);
            }

            BaseCombatEntity baseCombatEntity = baseEntity as BaseCombatEntity;
            if (baseCombatEntity != null)
            {
                float maxHealth;
                if (buildingBlock != null)
                    maxHealth = buildingBlock.currentGrade.maxHealth;
                else
                    maxHealth = baseCombatEntity.startHealth;

                baseCombatEntity.ResetLifeStateOnSpawn = false;
                baseCombatEntity.InitializeHealth(maxHealth * conditionMultiplier, maxHealth);
            }

            if (Interface.CallHook("OnConstructionPlace", baseEntity, component, placement, ownerPlayer) != null)
            {
                if (baseEntity.IsValid())
                    baseEntity.KillMessage();
                else
                    GameManager.Destroy(baseEntity, 0f);

                return null;
            }

            baseEntity.OnPlaced(ownerPlayer);
            baseEntity.OwnerID = ownerPlayer.userID;
            baseEntity.Spawn();

            StabilityEntity stabilityEntity = baseEntity as StabilityEntity;
            if (stabilityEntity != null)
                stabilityEntity.UpdateSurroundingEntities();

            // Return the GameObject of the entity that was created.
            return baseEntity.gameObject;
        }

        #endregion Oxide Hooks

        #region Harmony Patches

        [HarmonyPatch(typeof(Planner), "DoPlacement")]
        public static class DoPlacement_Patch
        {
            public static bool Prefix(Planner __instance, Construction.Target placement, Construction component, ref GameObject __result)
            {
                object hookResult = Interface.CallHook("OnCustomConstructionPlace", __instance, placement, component);

                if (hookResult is GameObject)
                {
                    // If the hook returned a GameObject, use it as the result and skip the original method.
                    __result = (GameObject)hookResult;
                    return false;
                }

                // Otherwise let the original method run.
                return true;
            }
        }

        #endregion Harmony Patches
    }
}