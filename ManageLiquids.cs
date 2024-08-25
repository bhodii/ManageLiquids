using System;
using System.Collections.Generic;
using System.Linq;
using XRL;
using XRL.Core;
using XRL.World;
using XRL.World.Parts;
using Bhodi_ManageLiquids.Utilities;

namespace Bhodi_ManageLiquids {
  [PlayerMutator]
  public class MyPlayerMutator : IPlayerMutator {
    public void mutate(GameObject player) {
      player.AddPart<BhodiManageLiquids>();
    }
  }

  [HasCallAfterGameLoadedAttribute]
  public class MyLoadGameHandler {
    [CallAfterGameLoadedAttribute]
    public static void MyLoadGameCallback() {
      GameObject player = XRLCore.Core?.Game?.Player?.Body;
        player?.RequirePart<BhodiManageLiquids>();
    }
  }
}

namespace XRL.World.Parts {
  [Serializable]
  public class BhodiManageLiquids : IScribedPart {

    private const string optionPrefix = "BhodiOptionManagesLiquid";
        
    private static bool IsManaged(string liquidName) {
      string liquidOptionName = "";
      if (!string.IsNullOrEmpty(liquidName)) {
        liquidOptionName = liquidName[..1].ToUpper() + liquidName[1..]; // Capitalize first letter for CamelCase option format
      }
      return XRL.UI.Options.GetOption(optionPrefix + liquidOptionName).EqualsNoCase("Yes");
    }

    private static int ManagedReserveAmount(string liquidName) {
      string liquidOptionName = "";
      if (!string.IsNullOrEmpty(liquidName)) {
        liquidOptionName = liquidName[..1].ToUpper() + liquidName[1..]; // Capitalize first letter for CamelCase option format
      }
      return (Int32.TryParse(XRL.UI.Options.GetOption(optionPrefix + liquidOptionName), out int result)) ? result : 0;
    }

    //private static void Logger(string message) {
    //  if (XRL.UI.Options.GetOption("OptionHarmonyDebug") == "Yes") {
    //    XRL.Messages.MessageQueue.AddPlayerMessage(message);
    //  }
    //}

    private static int CompareContainer(LiquidVolume vol1, LiquidVolume vol2) {
      // Highest Priority for Energy Cells 
      if (vol1.ParentObject.HasPart("LiquidFueledEnergyCell")) {
        return -1;
      } else if (vol2.ParentObject.HasPart("LiquidFueledEnergyCell")) {
        return 1;
      }
      // Deproiritize auto-collecting containers, except over phials
      if (vol1.ParentObject.GetPropertyOrTag("InventoryActionsAutoCollectLiquid") == "1" && vol2.MaxVolume > 1) {
        return 1;
      } else if (vol2.ParentObject.GetPropertyOrTag("InventoryActionsAutoCollectLiquid") == "1" && vol1.MaxVolume > 1) {
        return -1;
      }
      // Prioritize Equipped Camel Bladder
      if ((vol1.ParentObject.IsEquippedAsThrownWeapon() || vol1.ParentObject.IsWorn()) && vol1.ParentObject?.GetPart<AdjustLiquidWeightWhileWorn>()?.Factor < 1) {
        return -1;
      } else if ((vol2.ParentObject.IsEquippedAsThrownWeapon() || vol2.ParentObject.IsWorn()) && vol2.ParentObject?.GetPart<AdjustLiquidWeightWhileWorn>()?.Factor < 1) {
        return 1;
      }
      // Prioritize Suspensors
      if (vol1.ParentObject.GetPart<Suspensor>()?.PercentageForce > 0) {
        return -1;
      } else if (vol2.ParentObject.GetPart<Suspensor>()?.PercentageForce > 0) {
        return 1;
      }
      // Prioritize Containers that weigh less
      if (vol1.ParentObject.IntrinsicWeight != vol2.ParentObject.IntrinsicWeight) {
        return vol1.ParentObject.IntrinsicWeight - vol2.ParentObject.IntrinsicWeight;
      }
      // Default to Max Volume
      return vol2.MaxVolume - vol1.MaxVolume;
    }
    
    private static LiquidVolume GetEmptyContainer(List<GameObject> inventory = null, bool requireInOrganic = false, bool requireFireproof = false) {
      // Return the lightest, largest LiquidVolume not already auto-collecting
      var inv = inventory ?? The.Player.GetInventory();

      LiquidVolume newContainer = null;
      if (requireInOrganic) {
        newContainer = inv.Select(o => o.GetPart<LiquidVolume>())
                          .Where(lv => lv is not null
                                    && lv.IsEmpty()
                                    && !lv.ParentObject.HasPart("LiquidFueledEnergyCell")
                                    && lv.GetPropertyOrTag("InventoryActionsAutoCollectLiquid") != "1"
                                    && !lv.ParentObject.IsOrganic)
                          .OrderBy(o => o.ParentObject.IntrinsicWeight)
                          .ThenBy(o => o.MaxVolume)
                          .FirstOrDefault();
      } else if (requireFireproof) {
        newContainer = inv.Select(o => o.GetPart<LiquidVolume>())
                          .Where(lv => lv is not null
                                    && lv.IsEmpty()
                                    && !lv.ParentObject.HasPart("LiquidFueledEnergyCell")
                                    && lv.GetPropertyOrTag("InventoryActionsAutoCollectLiquid") != "1"
                                    && lv.ParentObject.Physics.FlameTemperature == 99999)
                          .OrderBy(o => o.ParentObject.IntrinsicWeight)
                          .ThenBy(o => o.MaxVolume)
                          .FirstOrDefault();
      } else {
        newContainer = inv.Select(o => o.GetPart<LiquidVolume>())
                          .Where(lv => lv is not null
                                    && lv.IsEmpty()
                                    && !lv.ParentObject.HasPart("LiquidFueledEnergyCell")
                                    && lv.GetPropertyOrTag("InventoryActionsAutoCollectLiquid") != "1")
                          .OrderBy(o => o.ParentObject.IntrinsicWeight)
                          .ThenBy(o => o.MaxVolume)
                          .FirstOrDefault();
      }
//      XRL.Messages.MessageQueue.AddPlayerMessage($"CFound: {newContainer?.ParentObject?.DisplayName} FuelLiquid:{newContainer?.ParentObject?.GetPart<LiquidFueledEnergyCell>()?.Liquid} Pure:{newContainer?.IsPureLiquid()}");
      
      return newContainer;
    }

    private static void TransferLiquid(LiquidVolume vol1, LiquidVolume vol2, int amount) {
      vol2.MixWith(Liquid: vol1, Amount: amount);
    }
    
    public static void MergeContainer(List<LiquidVolume> volumeList, int reserveDesired = 0, bool displayMessage = true) {
      volumeList.Sort(CompareContainer);
//      LogInfo($"MergeContainer: {volumeList[0].GetLiquidName()}:{volumeList[1].GetLiquidName()}:{volumeList.Count}");

      //foreach (var lv in volumeList) {
      //  LogInfo("Unsort:" + lv.ParentObject.DisplayName);
      //}

      // Merge all liquids to the front of the sorted list
      int j = 0;
      for (int i = 0; i < volumeList.Count; i++) {
        int needed = volumeList[i].MaxVolume - volumeList[i].Volume;
        j = i + 1;
        while (needed > 0 && j < volumeList.Count) {
          int xfer = Math.Min(volumeList[j].Volume, needed);
          TransferLiquid(volumeList[j], volumeList[i], xfer);
          needed -= xfer;
          j += 1;
        }
      }

      //foreach (var lv in volumeList) {
      //  LogInfo("Merged:" + lv.ParentObject.DisplayName);
      //}

      // Transfer the reserve
      int reserveAmount = 0;
      j = 0;
      for (int i = 0; i < volumeList.Count; i++) {
        if (volumeList[i].ParentObject.HasPart("LiquidFueledEnergyCell")) { j = i + 1; continue; }
//        LogInfo($"i:{i}:{volumeList[i].Volume} j:{j}:{volumeList[j].Volume} r:{reserveDesired}");
        if ((volumeList[i].Volume >= reserveDesired && volumeList[i].Volume <= volumeList[j].Volume)
          && !volumeList[j].ParentObject.HasPart("LiquidFueledEnergyCell")) {
          j = i;
        }
      }
//      LogInfo($"j:{j}:{volumeList[j].Volume} count-1:{volumeList.Count - 1}");
      if (j < volumeList.Count - 1) {
        int xfer = Math.Min(reserveDesired, volumeList[j + 1].MaxVolume - volumeList[j + 1].Volume);
//        LogInfo($"xfer {xfer}");
        TransferLiquid(volumeList[j], volumeList[j + 1], xfer);
        reserveAmount = volumeList[j + 1].Volume;
      }

      //foreach (var lv in volumeList) {
      //  LogInfo("Reserv:" + lv.ParentObject.DisplayName);
      //}

      if (displayMessage) {
        DisplayReport(volumeList, reserveDesired, reserveAmount);
      }
    }

    public void RearrangeAllLiquids(bool displayMessage = true) {
      if (displayMessage) {
        XRL.Messages.MessageQueue.AddPlayerMessage(DisplayRummage());
      }

      PlayWorldSound("Sounds/Interact/sfx_interact_liquidContainer_pourout");

      var inventory = The.Player.GetInventoryAndEquipment();

      var liquidContainersByType = inventory
        .Select(o => o?.GetPart<EnergyCellSocket>()?.Cell?.GetPart<LiquidVolume>() ?? o?.GetPart<LiquidVolume>()  )
        .Where(lv => lv is not null
                  && (lv.IsPureLiquid() || lv.ParentObject.HasPart("LiquidFueledEnergyCell"))
                  && !lv.EffectivelySealed()
                  && lv.ParentObject.Understood())
        .GroupBy(lv => lv.ParentObject?.GetPart<LiquidFueledEnergyCell>()?.Liquid ?? lv.GetPrimaryLiquid().ID)
        .ToDictionary(g => g.Key, g => g.ToList());

      foreach (var container in liquidContainersByType.Keys) {

        // If there's a heavy container or only one try to add an empty container to the list
        if (liquidContainersByType[container].Max(w => w.ParentObject.IntrinsicWeight > 1)
          || liquidContainersByType[container].Count <= 1) {
          LiquidVolume newContainer;
          if (container == "lava") {
            newContainer = GetEmptyContainer(inventory, requireFireproof: true);
          } else if (container == "acid") {
            newContainer = GetEmptyContainer(inventory, requireInOrganic: true);
          } else {
            newContainer = GetEmptyContainer(inventory);
          }
          liquidContainersByType[container].AddIfNotNull(newContainer);
        }

        if (liquidContainersByType[container].Count < 2) { continue; }
        switch (container) {
          // NOPE!
          case ("neutronflux"): break;

          case string liquid when IsManaged(liquid):
            MergeContainer(volumeList: liquidContainersByType[container],
                           reserveDesired: ManagedReserveAmount(liquid+"Reserve"),
                           displayMessage: displayMessage);
            break;
          
          default:
            var optionLiquids = new List<string>() { "water", "honey", "cider", "oil", "acid" };
            if (IsManaged("All") && !optionLiquids.Contains(container)) {
              MergeContainer(volumeList: liquidContainersByType[container],
                             reserveDesired: ManagedReserveAmount("AllReserve"),
                             displayMessage: displayMessage);
            }
            break;
        }
      }
    }

    private static void DisplayReport(List<LiquidVolume> volumeList, int reserveDesired = 0, int reserveAmount = 0) {
      int heavyCount = 0;
      string heavyMessage = "";
      for (int i = 0; i < volumeList.Count; i++) {
        if (volumeList[i].Volume > 0 && volumeList[i].ParentObject.IntrinsicWeight > 1 && !volumeList[i].ParentObject.HasPart("LiquidFueledEnergyCell")) {
          heavyCount++;
        }
      }
      if (heavyCount > 0) {
        heavyMessage = "{{r|*" + heavyCount + " Heavy* }}";
      }

      string reservedMessage = "";
      if (reserveDesired > 0) {
        reservedMessage = "(R:";
        if (reserveAmount == reserveDesired || volumeList.Any(v => v.Volume == reserveDesired)) {
          reservedMessage += reserveDesired;
        } else {
          reservedMessage += "{{r|" + reserveAmount + "}} of " + reserveDesired;
        }
        reservedMessage += ")";
      }

      XRL.Messages.MessageQueue.AddPlayerMessage("[{{" + volumeList[0].GetPrimaryLiquidColor() + "|" + volumeList[0].GetLiquidName() + "}}] " + volumeList.Sum(v => v.Volume) + " drams " + reservedMessage + heavyMessage);
    }

    private string DisplayRummage() {
      var verb = new List<string> { "search", "comb", "rifle", "poke", "rattle" }.ShuffleInPlace(ManageLiquids_Random.Rand);
      var noun = new List<string> { "equipment", "stuff", "gear", "pack", "inventory" }.ShuffleInPlace(ManageLiquids_Random.Rand);
      var verbing = new List<string> { "emptying", "draining", "spilling", "unloading", "dumping" }.ShuffleInPlace(ManageLiquids_Random.Rand);
      var amount = new List<string> { "some", "a little", "a bit", "a dab", "a drop", "a tad",
                                     "a part", "a portion", "a fraction", "a mite", "a pinch" }.ShuffleInPlace(ManageLiquids_Random.Rand);

      var thing = new List<string> { "this", "that" }.ShuffleInPlace(ManageLiquids_Random.Rand);

      return $"You {verb[0]} around in your {noun[0]}, {verbing[0]} {amount[0]} of {thing[0]} into {amount[1]} of {thing[1]}.";
    }
    public static void LogInfo(string message) {
//      UnityEngine.Debug.Log("INFO - " + message);
      XRL.Messages.MessageQueue.AddPlayerMessage(message);
    }

    public override bool WantEvent(int ID, int Propagation) {
      return base.WantEvent(ID, Propagation)
             || ID == OwnerGetInventoryActionsEvent.ID
             || ID == InventoryActionEvent.ID
          ;
    }
    public override bool HandleEvent(OwnerGetInventoryActionsEvent E) {
      if (E.Object.IsPlayer()) {
        E.AddAction(Name: "RearrangeLiquids", Display: "Rearrange Liquids", Command: "RearrangeAllLiquids", Key: 'g');
      }
      return base.HandleEvent(E);
    }
    public override bool HandleEvent(InventoryActionEvent E) {
      if (E.Command == "RearrangeAllLiquids") {
        E.RequestInterfaceExit();
        RearrangeAllLiquids(displayMessage: false);
        RearrangeAllLiquids(); // Once more with feeling to fill containers emptied in the first merge
      }
      return base.HandleEvent(E);
    }
  }
}
