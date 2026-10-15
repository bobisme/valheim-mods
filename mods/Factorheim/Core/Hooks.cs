using System;
using System.Linq;

namespace Factorheim
{
    // Hooking a piece's "destroyed" event so a hot reload can't break it. After a reload, PieceReload swaps standing pieces' components
    // for the new copy's, but the old component's handler stays on WearNTear.m_onDestroyed. It then runs on a destroyed object, throws,
    // and the throw stops the game removing the piece (the hammer only flashes it red). So: drop handlers whose object is gone, then add.
    internal static class Hooks
    {
        internal static void OnDestroyed(WearNTear wear,Action handler)
        {
            if(wear==null)return;
            Clean(wear);
            wear.m_onDestroyed+=handler;
        }
        internal static void Remove(WearNTear wear,Action handler)
        {
            if(wear!=null)wear.m_onDestroyed-=handler;
        }
        internal static void Clean(WearNTear wear)
        {
            if(wear?.m_onDestroyed==null)return;
            Delegate[] all=wear.m_onDestroyed.GetInvocationList();
            Delegate[] alive=all.Where(d=>!(d.Target is UnityEngine.Object o&&o==null)).ToArray();   // (Unity's null: destroyed)
            if(alive.Length!=all.Length)wear.m_onDestroyed=(Action)Delegate.Combine(alive);
        }
    }
}
