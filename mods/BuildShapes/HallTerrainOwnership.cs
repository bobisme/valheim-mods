using System;

namespace BuildShapes
{
    public sealed partial class Plugin
    {
        private static ZNetView HallGroundView(TerrainComp comp)
        {
            var view=comp?.GetComponent<ZNetView>();
            if(view==null || !view.IsValid())throw new ArgumentException("Basement terrain is not network-ready. Move closer and retry.");
            return view;
        }
        private static void LoadHallGround(TerrainComp comp)
        {HallGroundView(comp);HallLoad.Invoke(comp,null);}
        private static void HallOwner(TerrainComp comp,bool acquire=false)
        {
            var view=HallGroundView(comp);
            if(!view.IsOwner() && acquire)
            {
                // Refresh the synchronized ground before and after the native ownership transfer.
                HallLoad.Invoke(comp,null);view.ClaimOwnership();
            }
            if(!view.IsOwner())throw new ArgumentException(acquire?
                "Terrain ownership could not be acquired. Move closer and retry.":
                "Terrain ownership changed during the operation. Retry from Hallwright options; any saved recovery remains available.");
            HallLoad.Invoke(comp,null);
        }
    }
}
