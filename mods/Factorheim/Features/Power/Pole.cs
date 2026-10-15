using UnityEngine;

namespace Factorheim
{
    // A power pole: wires itself to poles in reach and powers machines in its supply area (PowerGrid does both). What kind it is comes
    // from its prefab name in Awake (a hot reload swaps this component for a fresh one: fields set on the prefab would be lost).
    internal sealed class Pole:MonoBehaviour,Hoverable
    {
        internal const int MaxWires=5;
        internal float Reach=7.5f,Supply=2.5f,Height=3.8f;

        private void Awake()
        {
            if(name.StartsWith("FH_IronPole")){Reach=30f;Supply=1f;Height=7.2f;}
            Live<Pole>.Add(this);
        }
        private void OnDestroy()=>Live<Pole>.Remove(this);
        internal Vector3 Top=>transform.position+transform.up*Height;

        public string GetHoverName()=>GetComponent<Piece>()?.m_name??name;
        public string GetHoverText()
        {
            string text=GetHoverName();
            PowerNet net=PowerGrid.Of(this);
            if(net==null)return Localization.instance.Localize(text+"\n<color=#ffd27a>Not wired yet</color>");
            text+=$"\nNetwork: {net.Poles.Count} poles, {net.Users.Count} machines, {net.Sources.Count} generators";
            text+=$"\nSupply: {Machine.Watts(net.Capacity)}   Demand: {Machine.Watts(net.Demand)}";
            string sat=net.Satisfaction>=0.999f?"#9cff8a":net.Satisfaction>=0.5f?"#ffd27a":"#ff7a6a";
            text+=$"\nSatisfaction: <color={sat}>{Mathf.RoundToInt(net.Satisfaction*100)}%</color>";
            if(net.Banks.Count>0)text+=$"\nStored: {Joules(net.Stored)} of {Joules(net.StoredMax)}"+(net.Flow>1?" (charging)":net.Flow<-1?" (draining)":"");
            text+=$"\nWire reach {Reach:0.#} m, supply area {Supply*2:0.#} m square";
            return Localization.instance.Localize(text);
        }
        public float GetHoverOffset()=>0f;
        internal static string Joules(float j)=>j>=1e6f?$"{j/1e6f:0.0} MJ":j>=1e3f?$"{j/1e3f:0} kJ":$"{j:0} J";
    }
}
