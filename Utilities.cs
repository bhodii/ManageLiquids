using System;
using XRL;
using XRL.Core;
using XRL.Rules;

namespace Bhodi_ManageLiquids.Utilities {
  [HasGameBasedStaticCache]
  public static class ManageLiquids_Random {
    private static Random _rand;
    public static Random Rand {
      get {
        if (_rand == null) {
          if (XRLCore.Core?.Game == null) {
            throw new Exception("Bhodi_ManageLiquids mod attempted to retrieve Random, but Game is not created yet.");
          } else if (XRLCore.Core.Game.IntGameState.ContainsKey("Bhodi_ManageLiquids:Random")) {
            int seed = XRLCore.Core.Game.GetIntGameState("Bhodi_ManageLiquids:Random");
            _rand = new Random(seed);
          } else {
            _rand = Stat.GetSeededRandomGenerator("Bhodi_ManageLiquids");
          }
          XRLCore.Core.Game.SetIntGameState("Bhodi_ManageLiquids:Random", _rand.Next());
        }
        return _rand;
      }
    }

    [GameBasedCacheInit]
    public static void ResetRandom() {
      _rand = null;
    }

    public static int Next(int min, int max) {
      return Rand.Next(min, max);
    }
  }
}