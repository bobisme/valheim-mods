using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Gary
{
    internal static class MapMarker
    {
        private static readonly AccessTools.FieldRef<Minimap,List<Minimap.PinData>> Pins=
            AccessTools.FieldRefAccess<Minimap,List<Minimap.PinData>>("m_pins");
        private static readonly AccessTools.FieldRef<Minimap,bool> RefreshPins=AccessTools.FieldRefAccess<Minimap,bool>("m_pinUpdateRequired");
        private static Minimap _map;
        private static Minimap.PinData _pin;
        private static ZNet _session;
        private static ZDOMan _data;
        private static long _playerID;
        private static ZDOID _garyID=ZDOID.None;
        private static float _nextFind;
        private static bool _loaded;

        internal static void Tick()
        {
            Minimap map=Minimap.instance;Player player=Player.m_localPlayer;
            if(Plugin.Instance==null||!Plugin.Instance.ShowMapMarker.Value||map==null||player==null||
                ZNet.instance==null||ZDOMan.instance==null||ZNetScene.instance==null)
            {Clear();return;}
            long playerID=player.GetPlayerID();
            if(_map!=map||_session!=ZNet.instance||_data!=ZDOMan.instance||_playerID!=playerID)
            {
                Clear();_map=map;_session=ZNet.instance;_data=ZDOMan.instance;_playerID=playerID;
            }
            ZDO gary=_data.GetZDO(_garyID);
            if(!IsMine(gary))gary=null;
            if(Time.unscaledTime>=_nextFind)
            {
                _nextFind=Time.unscaledTime+1;
                if(gary==null)
                {
                    // The same native index used by recall includes Gary's known, unloaded saved object.
                    foreach(ZDOID id in ZDOExtraData.GetAllZDOIDsWithHash(ZDOExtraData.Type.Long,Companion.Master.GetStableHashCode()))
                    {
                        ZDO candidate=_data.GetZDO(id);
                        if(!IsMine(candidate))continue;
                        gary=candidate;_garyID=id;break;
                    }
                }
                if(_pin!=null&&!Pins(map).Contains(_pin))Remove();
            }
            if(gary==null){Remove();return;}
            GameObject body=ZNetScene.instance.FindInstance(gary.m_uid);
            _loaded=body!=null;
            Vector3 position=_loaded?body.transform.position:gary.GetPosition();
            if(float.IsNaN(position.x)||float.IsNaN(position.y)||float.IsNaN(position.z)||
                float.IsInfinity(position.x)||float.IsInfinity(position.y)||float.IsInfinity(position.z))
            {Remove();return;}
            string name=_loaded?"Gary":"Gary (last seen)";
            // A transient player-style pin is excluded from map saving, cartography and normal pin editing.
            if(_pin==null)_pin=map.AddPin(position,Minimap.PinType.Player,name,false,false,0L);
            if(_pin.m_pos!=position){_pin.m_pos=position;RefreshPins(map)=true;}
            if(_pin.m_name!=name)
            {
                _pin.m_name=name;RefreshPins(map)=true;
                if(_pin.m_NamePinData?.PinNameText!=null)_pin.m_NamePinData.PinNameText.text=name;
            }
        }
        private static bool IsMine(ZDO z) => z!=null&&_playerID!=0&&z.GetPrefab()=="Greydwarf".GetStableHashCode()&&
            z.GetLong(Companion.Master,0)==_playerID;
        internal static void Tint(Minimap map)
        {
            if(map!=_map||_pin==null)return;
            var color=new Color(0.8f,0.45f,1f,_loaded?1f:0.65f);
            if(_pin.m_iconElement!=null)_pin.m_iconElement.color=color;
            if(_pin.m_NamePinData?.PinNameText!=null)_pin.m_NamePinData.PinNameText.color=color;
        }
        private static void Remove()
        {
            if(_map!=null&&_pin!=null)_map.RemovePin(_pin);
            _pin=null;_loaded=false;
        }
        internal static void Clear()
        {
            Remove();_map=null;_session=null;_data=null;_playerID=0;_garyID=ZDOID.None;_nextFind=0;
        }
    }
    [HarmonyPatch(typeof(Minimap),"UpdatePins")]
    internal static class GaryMapPinColor
    {
        // Native UpdatePins resets colors every frame; tint only Gary's own pin afterward.
        private static void Postfix(Minimap __instance)=>MapMarker.Tint(__instance);
    }
}
