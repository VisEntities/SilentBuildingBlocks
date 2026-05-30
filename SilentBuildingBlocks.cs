/*
 * Copyright (C) 2026 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using HarmonyLib;
using Oxide.Core;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Silent Building Blocks", "VisEntities", "1.1.0")]
    [Description("Removes the visual effect played when placing and upgrading building blocks.")]
    public class SilentBuildingBlocks : RustPlugin
    {
        #region Fields

        private static SilentBuildingBlocks _plugin;

        #endregion Fields

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
        }

        private void Unload()
        {
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

            return baseEntity.gameObject;
        }

        #endregion Oxide Hooks

        #region Harmony Patches

        [AutoPatch]
        [HarmonyPatch(typeof(Planner), "DoPlacement")]
        public static class DoPlacement_Patch
        {
            public static bool Prefix(Planner __instance, Construction.Target placement, Construction component, ref GameObject __result)
            {
                object hookResult = Interface.CallHook("OnCustomConstructionPlace", __instance, placement, component);

                if (hookResult is GameObject)
                {
                    __result = (GameObject)hookResult;
                    return false;
                }

                return true;
            }
        }

        [AutoPatch]
        [HarmonyPatch(typeof(BuildingBlock), "DoUpgradeToGrade")]
        public static class DoUpgradeToGrade_Patch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

                int stringIndex = -1;
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Ldstr && (codes[i].operand as string) == "DoUpgradeEffect")
                    {
                        stringIndex = i;
                        break;
                    }
                }

                if (stringIndex < 1)
                    return codes;

                int startIndex = stringIndex - 1;
                if (codes[startIndex].opcode != OpCodes.Ldarg_0)
                    return codes;

                int endIndex = -1;
                for (int i = stringIndex; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Call || codes[i].opcode == OpCodes.Callvirt)
                    {
                        MethodInfo method = codes[i].operand as MethodInfo;
                        if (method != null && method.Name == "ClientRPC")
                        {
                            endIndex = i;
                            break;
                        }
                    }
                }

                if (endIndex == -1)
                    return codes;

                for (int i = startIndex; i <= endIndex; i++)
                {
                    codes[i].opcode = OpCodes.Nop;
                    codes[i].operand = null;
                }

                return codes;
            }
        }

        #endregion Harmony Patches
    }
}