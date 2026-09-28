using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// main.js B2()/Ph() explosion backend, including the source ta()/Zt() moving-ship voxel path.
    /// TNT uses radius 4; creeper remains on its already-validated radius-3 path.
    /// </summary>
    public static class SourceExplosion
    {
        static readonly float[] Resistance=BuildResistance();

        public static void Explode(VoxelWorld world,MainPlayerController player,int ex,int ey,int ez,bool playerOwned=true,float radius=4f)
        {
            if(world==null)return;
            Vector3 damageCenter=new Vector3(ex,ey,ez), rayCenter=new Vector3(ex+.5f,ey+.5f,ez+.5f);
            MainSourceObjectRenderer.SetFallingHookSuppressed(true);
            if(ey>=VoxelConstants.MinY&&ey<=VoxelConstants.MaxY&&world.GetBlock(ex,ey,ez)!=BlockId.Air)
                world.SetBlock(ex,ey,ez,BlockId.Air,0,true);

            var hit=new HashSet<long>();var order=new List<long>(160);var shipHit=new Dictionary<int,HashSet<Vector3Int>>();
            for(int ix=0;ix<16;ix++)for(int iy=0;iy<16;iy++)for(int iz=0;iz<16;iz++)
            {
                if(ix!=0&&ix!=15&&iy!=0&&iy!=15&&iz!=0&&iz!=15)continue;
                Vector3 dir=new Vector3(ix/15f*2f-1f,iy/15f*2f-1f,iz/15f*2f-1f).normalized;
                float power=radius*(.7f+UnityEngine.Random.value*.6f);Vector3 p=rayCenter;
                while(power>0f)
                {
                    int x=Mathf.FloorToInt(p.x),y=Mathf.FloorToInt(p.y),z=Mathf.FloorToInt(p.z);
                    if(y<VoxelConstants.MinY||y>VoxelConstants.MaxY)break;
                    BlockId id=world.GetBlock(x,y,z);MainShipRuntime.ShipExplosionVoxel sv=default(MainShipRuntime.ShipExplosionVoxel);bool onShip=false;
                    if(id==BlockId.Air)onShip=MainShipRuntime.TryGetExplosionVoxel(p,out sv);
                    BlockId resistanceId=onShip?sv.Id:id;
                    if(resistanceId!=BlockId.Air)
                    {
                        power-=(BlastResistance(resistanceId)+.3f)*.3f;
                        if(power>0f&&resistanceId!=BlockId.Bedrock&&!BlockRegistry.IsWater(resistanceId))
                        {
                            if(onShip)
                            {
                                if(!shipHit.TryGetValue(sv.Handle,out HashSet<Vector3Int> set)){set=new HashSet<Vector3Int>();shipHit.Add(sv.Handle,set);}set.Add(sv.Block);
                            }
                            else{long key=Pack(x,y,z);if(hit.Add(key))order.Add(key);}
                        }
                    }
                    p+=dir*.3f;power-=.225f;
                }
            }

            for(int i=0;i<order.Count;i++)
            {
                int x,y,z;Unpack(order[i],out x,out y,out z);BlockId id=world.GetBlock(x,y,z);
                if(id==BlockId.Air||id==BlockId.Bedrock||BlockRegistry.IsWater(id))continue;
                if(id==BlockId.Tnt)
                {
                    MainSourceObjectRenderer.PrimeTnt(x,y,z,.1f+UnityEngine.Random.value*.25f);
                    continue;
                }
                world.SetBlock(x,y,z,BlockId.Air,0,true);
                if(UnityEngine.Random.value<.12f)MainTransientRenderer.SpawnBreakParticles(x,y,z,id,2);
            }

            MainShipRuntime.ApplyExplosionCells(shipHit);
            MainSourceObjectRenderer.SetFallingHookSuppressed(false);
            // main B2(): explicitly revisit the 9x9 x 10 high neighborhood for falling blocks.
            for(int dx=-4;dx<=4;dx++)for(int dz=-4;dz<=4;dz++)for(int y=ey-4;y<=ey+5;y++)
                MainSourceObjectRenderer.NotifyBlockChanged(ex+dx,y,ez+dz);

            const float entityRadius=8f;
            if(player!=null&&!player.IsCreative&&!player.Dead)
            {
                float d=Vector3.Distance(player.transform.position+Vector3.up*.9f,damageCenter);
                if(d<entityRadius)player.TakeMobDamage(SourceMath.JsRound(20f*(1f-d/entityRadius)));
            }
            for(int i=MobAI.Active.Count-1;i>=0;i--)
            {
                if(i>=MobAI.Active.Count)continue;MobAI m=MobAI.Active[i];if(m==null)continue;
                Vector3 mp=m.transform.position;float d=Vector3.Distance(mp+Vector3.up*(m.BodyHeight*.5f),damageCenter);
                if(d<entityRadius)m.Damage(SourceMath.JsRound(20f*(1f-d/entityRadius)),damageCenter,playerOwned,false);
            }
        }

        static float BlastResistance(BlockId id){int i=(int)id;return i>=0&&i<Resistance.Length?Resistance[i]:3f;}
        static float[] BuildResistance()
        {
            Array values=Enum.GetValues(typeof(BlockId));int max=0;foreach(BlockId id in values)if((int)id>max)max=(int)id;
            var t=new float[max+1];foreach(BlockId id in values)t[(int)id]=ForKey(SourceKey(id.ToString()));return t;
        }
        // Exact main.js $J(name) ordering. The ordering matters: e.g. GRASS_BLOCK matches GRASS_ (.3)
        // before the later generic GRASS (.5) rule, and SANDSTONE matches .8 before the soil rule.
        static float ForKey(string n)
        {
            if(n=="AIR")return 0f;
            if(Has(n,"BEDROCK","OBSIDIAN"))return 1200f;
            if(Has(n,"WATER","FLOW","LAVA"))return 100f;
            if(n=="TNT")return 0f;
            if(n.Contains("END_STONE"))return 9f;
            if(n.Contains("SANDSTONE"))return .8f;
            if(Has(n,"IRON","GOLD","DIAMOND","EMERALD","NETHERITE","ANVIL")||Has(n,"STONE","COBBLE","DEEPSLATE","BRICK","ANDESITE","DIORITE","GRANITE","TUFF","BLACKSTONE","TERRA","CONCRETE","QUARTZ","PRISMARINE","CALCITE","PURPUR","NETHERRACK","ORE","FURNACE","MAGMA"))return 6f;
            if(Has(n,"GLASS","ICE","LEAVES","WOOL","CARPET","FLOWER","SAPLING","FERN","BUSH","GRASS_","VINE","KELP","SEAGRASS","LILY","TORCH","SNOW"))return .3f;
            if(Has(n,"DIRT","GRASS","GRAVEL","PODZOL","CLAY","MUD")||n.EndsWith("SAND",StringComparison.Ordinal)||n.Contains("SAND_"))return .5f;
            return 3f;
        }
        static bool Has(string s,params string[] p){for(int i=0;i<p.Length;i++)if(s.Contains(p[i]))return true;return false;}
        static string SourceKey(string pascal){if(string.IsNullOrEmpty(pascal))return string.Empty;var b=new StringBuilder(pascal.Length+8);for(int i=0;i<pascal.Length;i++){char c=pascal[i];if(i>0&&char.IsUpper(c)&&!char.IsUpper(pascal[i-1]))b.Append('_');b.Append(char.ToUpperInvariant(c));}return b.ToString();}
        static long Pack(int x,int y,int z){unchecked{return((long)(x+1048576)<<42)^((long)(z+1048576)<<21)^(uint)(y-VoxelConstants.MinY);}}
        static void Unpack(long k,out int x,out int y,out int z){x=(int)((k>>42)&0x1fffff)-1048576;z=(int)((k>>21)&0x1fffff)-1048576;y=(int)(k&0x1fffff)+VoxelConstants.MinY;}
    }
}
